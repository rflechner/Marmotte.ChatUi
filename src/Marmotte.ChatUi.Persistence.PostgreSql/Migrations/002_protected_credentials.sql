CREATE TABLE chat.profile_credentials (
    owner_id varchar(256) NOT NULL,
    profile_id uuid NOT NULL,
    protected_value bytea NOT NULL,
    PRIMARY KEY (owner_id, profile_id),
    FOREIGN KEY (owner_id, profile_id) REFERENCES chat.profiles (owner_id, id) ON DELETE CASCADE
);
