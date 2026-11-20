import { createClient, RedisClientType } from "redis";
import pg from "pg";
import { config } from "../config.js";

export type IdempotencyRecord = {
  key: string;
  ticketId: string;
  decision: string;
  approverId: string;
  approverName?: string;
  approverAadId?: string;
  activityId?: string;
  conversationId?: string;
  reason?: string | null;
  timestamp: string;
  response?: unknown;
};

export interface IdempotencyStore {
  get(key: string): Promise<IdempotencyRecord | null>;
  set(record: IdempotencyRecord, ttlSeconds: number): Promise<void>;
}

class MemoryStore implements IdempotencyStore {
  private entries = new Map<string, { record: IdempotencyRecord; expiresAt: number }>();

  async get(key: string): Promise<IdempotencyRecord | null> {
    const entry = this.entries.get(key);
    if (!entry) {
      return null;
    }
    if (entry.expiresAt <= Date.now()) {
      this.entries.delete(key);
      return null;
    }
    return entry.record;
  }

  async set(record: IdempotencyRecord, ttlSeconds: number): Promise<void> {
    this.entries.set(record.key, {
      record,
      expiresAt: Date.now() + ttlSeconds * 1000,
    });
  }
}

class RedisStore implements IdempotencyStore {
  private client: RedisClientType;

  constructor(url: string) {
    this.client = createClient({ url });
    this.client.on("error", (err) => {
      // eslint-disable-next-line no-console
      console.error("Redis error", err);
    });
  }

  private async ensureConnected(): Promise<void> {
    if (!this.client.isOpen) {
      await this.client.connect();
    }
  }

  async get(key: string): Promise<IdempotencyRecord | null> {
    await this.ensureConnected();
    const value = await this.client.get(key);
    if (!value) {
      return null;
    }
    return JSON.parse(value) as IdempotencyRecord;
  }

  async set(record: IdempotencyRecord, ttlSeconds: number): Promise<void> {
    await this.ensureConnected();
    await this.client.setEx(record.key, ttlSeconds, JSON.stringify(record));
  }
}

class PostgresStore implements IdempotencyStore {
  private pool: pg.Pool;
  private ready = false;

  constructor(connectionString: string) {
    this.pool = new pg.Pool({ connectionString });
  }

  private async ensureTable(): Promise<void> {
    if (this.ready) {
      return;
    }
    await this.pool.query(`
      CREATE TABLE IF NOT EXISTS teams_bot_idempotency (
        idempotency_key text PRIMARY KEY,
        payload jsonb NOT NULL,
        created_at timestamptz NOT NULL DEFAULT now()
      );
    `);
    this.ready = true;
  }

  async get(key: string): Promise<IdempotencyRecord | null> {
    await this.ensureTable();
    const result = await this.pool.query(
      "SELECT payload FROM teams_bot_idempotency WHERE idempotency_key = $1",
      [key]
    );
    if (result.rowCount === 0) {
      return null;
    }
    return result.rows[0].payload as IdempotencyRecord;
  }

  async set(record: IdempotencyRecord, _ttlSeconds: number): Promise<void> {
    void _ttlSeconds;
    await this.ensureTable();
    await this.pool.query(
      `INSERT INTO teams_bot_idempotency (idempotency_key, payload)
       VALUES ($1, $2)
       ON CONFLICT (idempotency_key) DO UPDATE SET payload = $2`,
      [record.key, record]
    );
  }
}

export function createIdempotencyStore(): IdempotencyStore {
  if (config.postgresUrl) {
    return new PostgresStore(config.postgresUrl);
  }
  if (config.redisUrl) {
    return new RedisStore(config.redisUrl);
  }
  return new MemoryStore();
}
