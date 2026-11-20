from runtime_critic import evaluate


def test_evaluate_flags_empty_answer():
    result = evaluate(
        {
            "context": {},
            "response": {
                "answer": "",
                "execution_state": "completed",
                "execution_target": "shared_compute",
            },
        }
    )

    assert result["mode"] == "observe_only"
    assert any(finding["rule"] == "empty_answer" for finding in result["findings"])


def test_evaluate_flags_backend_language_in_everyday_answer():
    result = evaluate(
        {
            "context": {
                "intent_type": "practical_guidance",
                "presentation_template_family": "practical_guidance",
            },
            "response": {
                "answer": "I do not have enough governed evidence to answer this everyday question.",
                "execution_state": "completed",
                "execution_target": "shared_compute",
            },
        }
    )

    assert any(
        finding["rule"] == "backend_language_in_everyday_answer"
        for finding in result["findings"]
    )
    synthesis = next(score for score in result["scores"] if score["dimension"] == "synthesis_quality")
    assert synthesis["score"] < 1.0
