# Prompt Suite Smoke Tests

Run these smoke tests from the repository root with PowerShell:

```powershell
.\testing\prompt-suite\smoke-prompt-suite.ps1 .\testing\prompt-suite\rio-adversarial-followup-prompts.json
```

By default, the runner validates the output against `validation-criteria.json`. The script writes each run under `testing\runs\prompt-suite\<run-id>-<suite-name>\`.

Generated artifacts:

- `responses.json`: question, answer, trace, confidence, source groups, and route data.
- `analysis.json`: scored response analysis grouped by issue category.
- `validation.json`: machine-readable pass/fail gate for CI or release review.
- `report.md`: human-readable smoke-test report.

Useful switches:

- `-ValidationCriteriaPath <path>` uses a suite-specific validation gate.
- `-AllowValidationFailures` records artifacts but does not fail the PowerShell process.
- `-SkipValidation` runs the suite without applying a validation gate.

The validation gate is intended for repeatable regression checks. A passing smoke run should have no failed prompts, no prompts requiring fixes, no blocked issue categories, and all required trace fields present.
