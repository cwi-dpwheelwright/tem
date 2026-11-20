from edge_agent.backoff import BackoffPolicy, CircuitBreaker


def test_backoff_increases() -> None:
    policy = BackoffPolicy(base_seconds=1.0, max_seconds=10.0, multiplier=2.0, jitter=0.0)
    assert policy.next_delay(0) == 1.0
    assert policy.next_delay(1) == 2.0
    assert policy.next_delay(2) == 4.0
    assert policy.next_delay(10) == 10.0


def test_circuit_breaker_opens() -> None:
    breaker = CircuitBreaker(failure_threshold=2, recovery_seconds=60.0)
    assert breaker.allow_request()
    breaker.record_failure()
    assert breaker.allow_request()
    breaker.record_failure()
    assert breaker.status() == "open"
