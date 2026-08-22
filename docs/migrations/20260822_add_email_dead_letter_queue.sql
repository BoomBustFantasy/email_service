-- Dead-letter queue for outbound email.
--
-- Apply against the Supabase project before deploying the consumer: the C# side
-- calls dead_letter_email_message, read_email_dlq, replay_email_message and
-- get_email_dlq_depth, and they do not exist yet.
--
-- Why: nothing ever stopped retrying. pgmq redelivers on visibility timeout but
-- never gives up on its own, and the code comment claiming it dead-letters
-- automatically was wrong. In July 2026 a single message with an unconfigured
-- template key was retried 217 times over three days. PRD #17 calls for 3
-- attempts then dead-lettering, which needs somewhere to put the message and a
-- way to get it back.
--
-- Wrapper functions are SECURITY DEFINER, matching the existing
-- archive_email_message / read_email_queue pattern, so the service reaches pgmq
-- through public schema RPCs rather than needing direct access.

select pgmq.create('email_outbound_dlq');

-- Move a message off the main queue into the DLQ, preserving its payload so it
-- can be replayed unchanged. Returns the new DLQ msg_id, or null if the message
-- is already gone.
create or replace function public.dead_letter_email_message(
    p_msg_id bigint,
    p_reason text default null
)
returns bigint
language plpgsql
security definer
as $function$
declare
    v_message jsonb;
    v_dlq_msg_id bigint;
begin
    select message into v_message
    from pgmq.q_email_outbound
    where msg_id = p_msg_id;

    if v_message is null then
        return null;
    end if;

    select pgmq.send(
        'email_outbound_dlq',
        v_message || jsonb_build_object(
            'dead_lettered_at', now(),
            'dead_letter_reason', p_reason,
            'original_msg_id', p_msg_id
        )
    ) into v_dlq_msg_id;

    perform pgmq.archive('email_outbound', p_msg_id);

    return v_dlq_msg_id;
end;
$function$;

-- Read dead-lettered messages, for the admin replay endpoint.
create or replace function public.read_email_dlq(
    p_batch_size integer default 10,
    p_visibility_timeout_seconds integer default 30
)
returns table(msg_id bigint, read_ct integer, enqueued_at timestamptz, vt timestamptz, message jsonb)
language plpgsql
security definer
as $function$
begin
    return query
    select * from pgmq.read('email_outbound_dlq', p_visibility_timeout_seconds, p_batch_size);
end;
$function$;

-- Put a dead-lettered message back on the main queue. The idempotency key rides
-- along in the payload untouched, so a message that did in fact send is caught
-- by the idempotency gate rather than delivered twice.
create or replace function public.replay_email_message(p_dlq_msg_id bigint)
returns bigint
language plpgsql
security definer
as $function$
declare
    v_message jsonb;
    v_new_msg_id bigint;
begin
    select message into v_message
    from pgmq.q_email_outbound_dlq
    where msg_id = p_dlq_msg_id;

    if v_message is null then
        return null;
    end if;

    select pgmq.send(
        'email_outbound',
        (v_message - 'dead_lettered_at' - 'dead_letter_reason' - 'original_msg_id')
            || jsonb_build_object('replayed_at', now())
    ) into v_new_msg_id;

    perform pgmq.archive('email_outbound_dlq', p_dlq_msg_id);

    return v_new_msg_id;
end;
$function$;

-- Dead-letter depth, for /metrics and the backlog alert.
create or replace function public.get_email_dlq_depth()
returns bigint
language sql
security definer
as $function$
    select count(*) from pgmq.q_email_outbound_dlq;
$function$;
