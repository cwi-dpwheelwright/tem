# CWI Chat History Import Memory Scope Test

Upload only this ZIP in the GPT site Import Chat History flow:

- CWI_Chat_History_Memory_Scope_Test.zip

Do not upload this README as an import file. The ZIP intentionally contains only `conversations.json` so the test instructions do not become user memory.

## Automated Smoke Harness

Run the same backend contract used by the Import Chat History button with:

```powershell
$env:CWI_SMOKE_SSH_TARGET = "<ssh-target-that-can-reach-cwi-stack>"
powershell -NoProfile -ExecutionPolicy Bypass -File testing\chat-history-upload\smoke-chat-history-upload.ps1 testing\chat-history-upload\CWI_Chat_History_Memory_Scope_Test.zip
```

The only required input is the payload path. The payload may be the chat-history ZIP or a supported text/JSON payload.
Runtime access details are supplied through environment variables so run evidence does not store secrets or private hostnames.

The harness writes run evidence under:

```text
testing\runs\chat-history-upload\<run-id>\
```

Each API transaction gets its own folder so the request payload and response are visibly paired:

```text
testing\runs\chat-history-upload\<run-id>\
  upload\
    part-00001\
      payload.json
      response.json
      meta.json
  ask\
    importing-what-do-you-know\
      payload.json
      response.json
      meta.json
```

Run directories also include `analysis.json`, `source-summary.json`, `auth-context.json`, and `report.md`. They should not contain runner scripts or one-off Python files.

## What This Tests

1. The ZIP extractor reads `conversations.json`.
2. The chat-history normalizer converts structured messages into transcript text.
3. The import is large enough to force frontend multi-part chunking with the default 500,000 character chunk size.
4. Backend ingestion creates source chunks and activates governed user memory.
5. Imported memory affects answers for the importing user.
6. Imported memory does not affect other users.

## Distinct Test Facts

The importing user should be able to retrieve these facts after import:

- Identity marker: Avery Testwell
- Private verification phrase: BLUELANTERN-7429
- Project: Project Quartz Lantern
- Early open loop: OPEN-LOOP-GAMMA-17
- Late chunk open loop: OPEN-LOOP-NEBULA-88
- Late chunk project marker: LATE-CHUNK-MARIGOLD-204
- Decision: DECISION-EMBER-31
- Home/personal marker: HOME-ORCHID-58
- Goal marker: GOAL-AURORA-19
- People: Demitrius, Chad, Jasmin

## Same-User Validation Prompts

Ask these as the same user who imported the ZIP:

1. `what do you know about me?`
   Expected: mentions Avery Testwell, Project Quartz Lantern, BLUELANTERN-7429, key people, confidence, and memory/provenance sources.

2. `what projects am I working on?`
   Expected: mentions Project Quartz Lantern and CWI memory activation.

3. `who are the key people in my network?`
   Expected: mentions Demitrius, Chad, and Jasmin with their roles.

4. `what open loops do I have?`
   Expected: mentions OPEN-LOOP-GAMMA-17 and OPEN-LOOP-NEBULA-88. OPEN-LOOP-NEBULA-88 proves later import chunks affected runtime memory.

5. `show sources for what you know about me`
   Expected: returns memory/provenance source references from the imported chat history.

6. `what do you know about my home life?`
   Expected: mentions HOME-ORCHID-58 only if personal memory is allowed for that user/session.

## Cross-User Isolation Validation

Log in as a different user who did not import this ZIP, then ask:

1. `what do you know about me?`
2. `what open loops do I have?`
3. `show sources for what you know about me`

Expected for the different user:

- The response should not mention Avery Testwell.
- The response should not mention BLUELANTERN-7429.
- The response should not mention Project Quartz Lantern.
- The response should not mention OPEN-LOOP-GAMMA-17, OPEN-LOOP-NEBULA-88, LATE-CHUNK-MARIGOLD-204, DECISION-EMBER-31, HOME-ORCHID-58, or GOAL-AURORA-19.
- It should either show that no memory exists for that user or only show memory imported/owned by that specific user.

## Acceptance

Pass only if the importing user can retrieve the facts with trace/provenance, and another user cannot retrieve those facts.
