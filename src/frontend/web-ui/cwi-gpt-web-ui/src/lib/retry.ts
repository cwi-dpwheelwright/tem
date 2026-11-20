export interface RetryOptions {
  method?: string;
  maxAttempts: number;
  baseDelayMs: number;
  maxDelayMs: number;
  jitterRatio?: number;
  shouldRetry?: (error: unknown) => boolean;
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function jitter(delay: number, ratio: number): number {
  const jitterAmount = delay * ratio;
  const offset = (Math.random() * 2 - 1) * jitterAmount;
  return Math.max(0, delay + offset);
}

export async function retryWithBackoff<T>(
  fn: () => Promise<T>,
  options: RetryOptions,
): Promise<T> {
  const {
    maxAttempts,
    baseDelayMs,
    maxDelayMs,
    jitterRatio = 0,
    shouldRetry = () => true,
  } = options;

  let attempt = 0;
  let lastError: unknown = null;

  while (attempt < maxAttempts) {
    attempt += 1;
    try {
      return await fn();
    } catch (err) {
      lastError = err;
      if (attempt >= maxAttempts || !shouldRetry(err)) {
        break;
      }
      const delay = Math.min(maxDelayMs, baseDelayMs * Math.pow(2, attempt - 1));
      const waitMs = jitterRatio > 0 ? jitter(delay, jitterRatio) : delay;
      await sleep(waitMs);
    }
  }

  throw lastError ?? new Error("Retry attempts exhausted.");
}
