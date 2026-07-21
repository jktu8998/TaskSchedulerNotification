-- ============================================================
--  V001: Создание начальной схемы Task Scheduler
-- ============================================================

-- 1. Основная таблица заданий
CREATE TABLE IF NOT EXISTS tasks (
    id                      UUID PRIMARY KEY,
    chain_id                UUID NULL REFERENCES job_chains(id) ON DELETE SET NULL,
    chain_step_index        INTEGER NULL,
    sender_id               VARCHAR(128) NOT NULL,
    idempotency_key         VARCHAR(128) NOT NULL,
    type                    INTEGER NOT NULL,          -- TaskType enum
    status                  INTEGER NOT NULL,          -- StatusTask enum
    schedule                JSONB NOT NULL,
    execution               JSONB NOT NULL,
    result_delivery         JSONB NULL,
    polling_config          JSONB NULL,
    retry_policy            JSONB NOT NULL,
    encrypted_sensitive_data TEXT NULL,
    raw_payload             JSONB NOT NULL DEFAULT '{}',
    created_at              TIMESTAMPTZ NOT NULL,
    updated_at              TIMESTAMPTZ NULL,
    next_execution_at       TIMESTAMPTZ NULL,
    locked_until            TIMESTAMPTZ NULL,
    scheduled_at            TIMESTAMPTZ NULL,
    current_attempt         INTEGER NOT NULL DEFAULT 0,
    version                 INTEGER NOT NULL DEFAULT 1,
    metadata                JSONB NULL
    );

-- 1. Новая таблица для хранения цепочек
CREATE TABLE IF NOT EXISTS job_chains (
    id                  UUID PRIMARY KEY,
    sender_id           VARCHAR(128) NOT NULL,
    name                VARCHAR(256) NOT NULL,
    description         TEXT NULL,
    status              INTEGER NOT NULL,          -- ChainStatus enum
    steps               JSONB NOT NULL,            -- массив ChainStep
    current_step_index  INTEGER NOT NULL DEFAULT -1,
    current_task_id     UUID NULL REFERENCES tasks(id) ON DELETE SET NULL,
    created_at          TIMESTAMPTZ NOT NULL,
    updated_at          TIMESTAMPTZ NULL,
    version             INTEGER NOT NULL DEFAULT 1
    );

-- 2. Таблица исходящих сообщений (Transactional Outbox)
CREATE TABLE IF NOT EXISTS outbox_messages (
    id              UUID PRIMARY KEY,
    task_id         UUID NOT NULL,
    event_type      VARCHAR(256) NOT NULL,
    payload         JSONB NULL,
    created_at      TIMESTAMPTZ NOT NULL,
    retry_count     INTEGER NOT NULL DEFAULT 0,
    max_retries     INTEGER NOT NULL DEFAULT 3
    );

-- 3. Таблица Dead Letter Queue
CREATE TABLE IF NOT EXISTS dead_letter_queue (
    id                      BIGSERIAL PRIMARY KEY,
    task_id                 UUID NOT NULL,
    sender_id               VARCHAR(128) NOT NULL,
    original_task_snapshot  JSONB NOT NULL,
    error_details           TEXT NULL,
    moved_at                TIMESTAMPTZ NOT NULL
    );

-- 4. Таблица состояний для polling-заданий
CREATE TABLE IF NOT EXISTS polling_states (
   task_id             UUID PRIMARY KEY,
   last_response_json  TEXT NULL,
   last_checked_at     TIMESTAMPTZ NULL
);

-- 5. Таблица логов заданий
CREATE TABLE IF NOT EXISTS task_logs (
    id          BIGSERIAL PRIMARY KEY,
    task_id     UUID NOT NULL,
    timestamp   TIMESTAMPTZ NOT NULL,
    event_type  VARCHAR(256) NOT NULL,
    message     TEXT NULL,
    details     TEXT NULL
    );

-- ============================================================
-- Индексы для производительности и ограничения
-- ============================================================

-- Уникальный ключ идемпотентности (гарантирует exactly-once создание)
CREATE UNIQUE INDEX IF NOT EXISTS ux_tasks_idempotency_key ON tasks (idempotency_key);

-- Поиск заданий по отправителю с фильтрацией
CREATE INDEX IF NOT EXISTS ix_tasks_sender_status ON tasks (sender_id, status, created_at DESC);

-- Планировщик: поиск заданий, готовых к выполнению
CREATE INDEX IF NOT EXISTS ix_tasks_scheduled_next ON tasks (status, next_execution_at)
    WHERE status = 1; -- Scheduled

-- Heartbeat: поиск зависших заданий
CREATE INDEX IF NOT EXISTS ix_tasks_executing_locked ON tasks (status, locked_until)
    WHERE status = 3; -- Executing

-- Polling-задания
CREATE INDEX IF NOT EXISTS ix_tasks_polling_scheduled ON tasks (type, status, next_execution_at)
    WHERE type = 3 AND status = 1; -- Polling + Scheduledc

-- Индексы для производительности цепочек заданий 
CREATE INDEX IF NOT EXISTS ix_job_chains_sender ON job_chains (sender_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_job_chains_status ON job_chains (status) WHERE status = 1; -- Activeс
-- Для быстрого поиска заданий, принадлежащих цепочке
CREATE INDEX IF NOT EXISTS ix_tasks_chain_id ON tasks (chain_id) WHERE chain_id IS NOT NULL;

-- Outbox: выборка необработанных сообщений
CREATE INDEX IF NOT EXISTS ix_outbox_created ON outbox_messages (created_at, event_type);

-- Логи: быстрый доступ по заданию
CREATE INDEX IF NOT EXISTS ix_task_logs_task_id ON task_logs (task_id, timestamp);

-- DLQ: фильтрация по отправителю
CREATE INDEX IF NOT EXISTS ix_dlq_sender ON dead_letter_queue (sender_id, moved_at DESC);