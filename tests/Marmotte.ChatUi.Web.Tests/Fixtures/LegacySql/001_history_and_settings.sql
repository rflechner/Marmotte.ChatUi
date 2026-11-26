CREATE SCHEMA IF NOT EXISTS chat;

CREATE TABLE chat.conversations (
    id uuid PRIMARY KEY,
    owner_id varchar(256) NOT NULL,
    title varchar(200) NOT NULL CHECK (length(title) > 0),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL CHECK (updated_at >= created_at),
    revision bigint NOT NULL CHECK (revision > 0),
    UNIQUE (owner_id, id)
);
CREATE INDEX conversations_owner_page ON chat.conversations (owner_id, created_at, id);

CREATE TABLE chat.messages (
    owner_id varchar(256) NOT NULL,
    conversation_id uuid NOT NULL,
    id uuid NOT NULL,
    submission_id uuid NOT NULL,
    sequence bigint NOT NULL CHECK (sequence > 0),
    role integer NOT NULL CHECK (role BETWEEN 0 AND 1),
    content text NOT NULL,
    state integer NOT NULL CHECK (state BETWEEN 0 AND 4),
    failure integer CHECK (failure BETWEEN 0 AND 4),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL CHECK (updated_at >= created_at),
    PRIMARY KEY (owner_id, conversation_id, id),
    UNIQUE (owner_id, conversation_id, sequence),
    UNIQUE (owner_id, conversation_id, submission_id, role),
    FOREIGN KEY (owner_id, conversation_id) REFERENCES chat.conversations (owner_id, id) ON DELETE CASCADE,
    CHECK ((state = 4) = (failure IS NOT NULL)),
    CHECK (role <> 0 OR state = 2),
    CHECK (state <> 0 OR content = '')
);
CREATE UNIQUE INDEX messages_one_active_response ON chat.messages (owner_id, conversation_id) WHERE role = 1 AND state IN (0, 1);

CREATE TABLE chat.attachments (
    id uuid PRIMARY KEY,
    owner_id varchar(256) NOT NULL,
    file_name varchar(255) NOT NULL,
    media_type varchar(127) NOT NULL,
    size_bytes bigint NOT NULL CHECK (size_bytes > 0),
    created_at timestamptz NOT NULL,
    state integer NOT NULL CHECK (state BETWEEN 0 AND 3),
    width integer CHECK (width > 0),
    height integer CHECK (height > 0),
    UNIQUE (owner_id, id),
    CHECK ((width IS NULL) = (height IS NULL))
);
CREATE TABLE chat.message_attachments (
    owner_id varchar(256) NOT NULL,
    conversation_id uuid NOT NULL,
    message_id uuid NOT NULL,
    attachment_id uuid NOT NULL,
    position integer NOT NULL CHECK (position >= 0),
    PRIMARY KEY (owner_id, conversation_id, message_id, attachment_id),
    UNIQUE (owner_id, conversation_id, message_id, position),
    FOREIGN KEY (owner_id, conversation_id, message_id) REFERENCES chat.messages (owner_id, conversation_id, id) ON DELETE CASCADE,
    FOREIGN KEY (owner_id, attachment_id) REFERENCES chat.attachments (owner_id, id)
);
CREATE INDEX message_attachments_reference ON chat.message_attachments (owner_id, attachment_id);

CREATE TABLE chat.profiles (
    id uuid PRIMARY KEY,
    owner_id varchar(256) NOT NULL,
    name varchar(100) NOT NULL,
    endpoint text NOT NULL,
    model varchar(256) NOT NULL,
    temperature double precision CHECK (temperature >= 0 AND temperature < 'Infinity'::double precision),
    max_output_tokens integer CHECK (max_output_tokens > 0),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL CHECK (updated_at >= created_at),
    UNIQUE (owner_id, id)
);
CREATE INDEX profiles_owner_page ON chat.profiles (owner_id, created_at, id);
CREATE TABLE chat.preferences (
    owner_id varchar(256) PRIMARY KEY,
    theme integer NOT NULL CHECK (theme BETWEEN 0 AND 2),
    selected_profile_id uuid,
    selected_model varchar(256),
    FOREIGN KEY (owner_id, selected_profile_id) REFERENCES chat.profiles (owner_id, id),
    CHECK (selected_profile_id IS NOT NULL OR selected_model IS NULL)
);
