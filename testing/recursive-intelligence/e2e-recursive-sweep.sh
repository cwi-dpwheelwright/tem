#!/usr/bin/env bash
set -Eeuo pipefail

PUBLIC_BASE_URL="${CWI_E2E_PUBLIC_BASE_URL:-https://cwigpt.com}"
ORCHESTRATOR_BASE_URL="${CWI_E2E_ORCHESTRATOR_BASE_URL:-http://127.0.0.1:18085}"
IDENTITY_BASE_URL="${CWI_E2E_IDENTITY_BASE_URL:-http://127.0.0.1:8085}"
IDENTITY_CONTAINER="${CWI_E2E_IDENTITY_CONTAINER:-internal-01-identity-server}"
CRITIC_BASE_URL="${CWI_E2E_CRITIC_BASE_URL:-http://100.107.108.40:8086}"
OUTPUT_ROOT="${CWI_E2E_OUTPUT_ROOT:-/tmp/cwi-recursive-e2e-reports}"

PUBLIC_BASE_URL="${PUBLIC_BASE_URL%/}"
ORCHESTRATOR_BASE_URL="${ORCHESTRATOR_BASE_URL%/}"
IDENTITY_BASE_URL="${IDENTITY_BASE_URL%/}"
CRITIC_BASE_URL="${CRITIC_BASE_URL%/}"

RUN_SUFFIX="$(date +%s%N)"
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)-$RUN_SUFFIX"
RUN_DIR="$OUTPUT_ROOT/$RUN_ID"
CHECKS_PATH="$RUN_DIR/checks.ndjson"
SUMMARY_PATH="$RUN_DIR/summary.json"
REPORT_PATH="$RUN_DIR/report.md"
SMOKE_USER_ID=""
ADMIN_TOKEN=""

mkdir -p "$RUN_DIR"

add_check() {
  local name="$1"
  local status="$2"
  local detail="${3:-}"
  jq -nc --arg name "$name" --arg status "$status" --arg detail "$detail" \
    '{name:$name,status:$status,detail:$detail}' >> "$CHECKS_PATH"
}

read_env() {
  docker exec "$IDENTITY_CONTAINER" printenv "$1" 2>/dev/null | tr -d '\r' || true
}

login() {
  local tenant="$1"
  local username="$2"
  local password="$3"

  jq -nc --arg tenant_id "$tenant" --arg username "$username" --arg password "$password" \
    '{tenant_id:$tenant_id,username:$username,password:$password}' |
    curl -sS "$IDENTITY_BASE_URL/auth/login" \
      -H 'Content-Type: application/json' \
      --data-binary @- |
    jq -er '.access_token'
}

cleanup() {
  if [[ -n "$SMOKE_USER_ID" && -n "$ADMIN_TOKEN" ]]; then
    jq -nc --arg tenant_id "$ADMIN_TENANT" --arg reason "recursive intelligence e2e cleanup" \
      '{tenant_id:$tenant_id,reason:$reason}' |
      curl -sS "$IDENTITY_BASE_URL/admin/users/$SMOKE_USER_ID/remove" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        -H 'Content-Type: application/json' \
        --data-binary @- >/dev/null || true
  fi
}
trap cleanup EXIT

touch "$CHECKS_PATH"

if curl -sS -f "$PUBLIC_BASE_URL/api/ready" >/dev/null; then
  add_check "public_ready" "PASS" "ready endpoint returned success"
else
  add_check "public_ready" "FAIL" "ready endpoint was not reachable"
fi

unauth_status="$(curl -sS -o "$RUN_DIR/unauth-ask.json" -w '%{http_code}' \
  -X POST "$PUBLIC_BASE_URL/api/ask" || true)"
if [[ "$unauth_status" == "401" ]]; then
  add_check "ask_requires_auth" "PASS" "unauthenticated ASK returned 401"
else
  add_check "ask_requires_auth" "FAIL" "unauthenticated ASK returned $unauth_status"
fi

ADMIN_TENANT="$(read_env IDENTITY_SUPERADMIN_TENANT_ID)"
ADMIN_USERNAME="$(read_env IDENTITY_SUPERADMIN_USERNAME)"
ADMIN_PASSWORD="$(read_env IDENTITY_SUPERADMIN_PASSWORD)"
if [[ -z "$ADMIN_TENANT" || -z "$ADMIN_USERNAME" || -z "$ADMIN_PASSWORD" ]]; then
  add_check "identity_superadmin_env" "FAIL" "identity container does not expose required smoke env"
else
  add_check "identity_superadmin_env" "PASS" "required smoke env is present"
fi

if [[ -n "$ADMIN_TENANT" && -n "$ADMIN_USERNAME" && -n "$ADMIN_PASSWORD" ]]; then
  if ADMIN_TOKEN="$(login "$ADMIN_TENANT" "$ADMIN_USERNAME" "$ADMIN_PASSWORD")"; then
    add_check "admin_login" "PASS" "admin login succeeded"
  else
    add_check "admin_login" "FAIL" "admin login failed"
  fi
fi

if [[ -n "$ADMIN_TOKEN" ]]; then
  SMOKE_USERNAME="recursive-e2e-$RUN_SUFFIX"
  SMOKE_EMAIL="$SMOKE_USERNAME@smoke.invalid"
  SMOKE_PASSWORD="RecursiveE2e!$RUN_SUFFIX"

  invite_status="$(jq -nc \
      --arg tenant_id "$ADMIN_TENANT" \
      --arg username "$SMOKE_USERNAME" \
      --arg email "$SMOKE_EMAIL" \
      --arg display_name "Recursive E2E Smoke" \
      '{tenant_id:$tenant_id,username:$username,email:$email,display_name:$display_name}' |
    curl -sS -o "$RUN_DIR/invite.json" -w '%{http_code}' \
      "$IDENTITY_BASE_URL/admin/users/invite" \
      -H "Authorization: Bearer $ADMIN_TOKEN" \
      -H 'Content-Type: application/json' \
      --data-binary @- || true)"

  if [[ "$invite_status" =~ ^2 ]]; then
    add_check "smoke_user_invite" "PASS" "temporary smoke user invite accepted"
  else
    add_check "smoke_user_invite" "FAIL" "invite returned $invite_status"
  fi

  SMOKE_USER_ID="$(curl -sS "$IDENTITY_BASE_URL/admin/users?tenant_id=$(printf '%s' "$ADMIN_TENANT" | jq -sRr @uri)" \
      -H "Authorization: Bearer $ADMIN_TOKEN" |
    jq -er --arg email "$SMOKE_EMAIL" '.[] | select(.email == $email) | .user_id' | head -n 1 || true)"

  if [[ -n "$SMOKE_USER_ID" ]]; then
    add_check "smoke_user_lookup" "PASS" "temporary smoke user found"
  else
    add_check "smoke_user_lookup" "FAIL" "temporary smoke user lookup failed"
  fi

  if [[ -n "$SMOKE_USER_ID" ]]; then
    approve_status="$(jq -nc --arg tenant_id "$ADMIN_TENANT" '{tenant_id:$tenant_id}' |
      curl -sS -o "$RUN_DIR/approve.json" -w '%{http_code}' \
        "$IDENTITY_BASE_URL/admin/users/$SMOKE_USER_ID/approve" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        -H 'Content-Type: application/json' \
        --data-binary @- || true)"

    password_status="$(jq -nc --arg tenant_id "$ADMIN_TENANT" --arg password "$SMOKE_PASSWORD" \
        '{tenant_id:$tenant_id,password:$password}' |
      curl -sS -o "$RUN_DIR/password.json" -w '%{http_code}' \
        "$IDENTITY_BASE_URL/admin/users/$SMOKE_USER_ID/password" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        -H 'Content-Type: application/json' \
        --data-binary @- || true)"

    [[ "$approve_status" =~ ^2 ]] && add_check "smoke_user_approve" "PASS" "temporary smoke user approved" || add_check "smoke_user_approve" "FAIL" "approve returned $approve_status"
    [[ "$password_status" =~ ^2 ]] && add_check "smoke_user_password" "PASS" "temporary smoke user password set" || add_check "smoke_user_password" "FAIL" "password set returned $password_status"

    SMOKE_TOKEN="$(login "$ADMIN_TENANT" "$SMOKE_USERNAME" "$SMOKE_PASSWORD" || true)"
    if [[ -n "$SMOKE_TOKEN" ]]; then
      add_check "smoke_user_login" "PASS" "temporary smoke user login succeeded"

      jq -nc \
        --arg tenant_id "$ADMIN_TENANT" \
        --arg user_id "$SMOKE_USER_ID" \
        --arg question "What is the capital of Texas?" \
        --arg conversation_id "recursive-e2e-$RUN_SUFFIX" \
        '{tenant_id:$tenant_id,user_id:$user_id,question:$question,pack:{name:"personal"},context:{mode:"observe",top_k:8,timezone:"America/Chicago",conversation_id:$conversation_id}}' \
        > "$RUN_DIR/ask-payload.json"

      ask_status="$(curl -sS -o "$RUN_DIR/ask-response.json" -w '%{http_code}' \
        "$PUBLIC_BASE_URL/api/ask" \
        -H "Authorization: Bearer $SMOKE_TOKEN" \
        -H 'Accept: application/json' \
        -H 'Content-Type: application/json' \
        --data-binary @"$RUN_DIR/ask-payload.json" || true)"

      if [[ "$ask_status" == "200" ]]; then
        add_check "ask_authenticated_status" "PASS" "authenticated ASK returned 200"
      else
        add_check "ask_authenticated_status" "FAIL" "authenticated ASK returned $ask_status"
      fi

      if jq -e '.trace.sources != null and (.trace.confidence|type == "number") and (.trace.policy_mode|type == "string") and (.trace.model|type == "string") and (.trace.pack_hash|type == "string")' "$RUN_DIR/ask-response.json" >/dev/null 2>&1; then
        add_check "ask_trace_contract" "PASS" "required trace fields present"
      else
        add_check "ask_trace_contract" "FAIL" "required trace fields missing or wrong type"
      fi

      answer_text="$(jq -r '.answer // .answer_markdown // ""' "$RUN_DIR/ask-response.json" 2>/dev/null || true)"
      if [[ "$answer_text" =~ [Aa]ustin ]]; then
        add_check "ask_answer_sanity" "PASS" "capital answer contained Austin"
      else
        add_check "ask_answer_sanity" "FAIL" "capital answer did not contain expected direct answer"
      fi

      if grep -Eiq 'activated user memory|governed memory|from the available context|something broke|graph data|proactive intelligence' "$RUN_DIR/ask-response.json"; then
        add_check "ask_no_backend_language" "FAIL" "response leaked backend/framework language"
      else
        add_check "ask_no_backend_language" "PASS" "response did not match known backend leak markers"
      fi
    else
      add_check "smoke_user_login" "FAIL" "temporary smoke user login failed"
    fi
  fi

  cap_status="$(curl -sS -o "$RUN_DIR/runtime-capabilities.json" -w '%{http_code}' \
    "$ORCHESTRATOR_BASE_URL/api/admin/runtime-capabilities" \
    -H "Authorization: Bearer $ADMIN_TOKEN" || true)"
  if [[ "$cap_status" == "200" ]]; then
    add_check "runtime_capability_matrix" "PASS" "runtime capability matrix returned 200"
    if jq -e '
      (map(.id) | contains([
        "teacher",
        "critic",
        "process_miner",
        "graph",
        "vector",
        "truth_engine",
        "cache",
        "federated_learning",
        "external_opinion"
      ]))
    ' "$RUN_DIR/runtime-capabilities.json" >/dev/null 2>&1; then
      add_check "runtime_capability_matrix_contract" "PASS" "matrix includes expected recursive capability IDs"
    else
      add_check "runtime_capability_matrix_contract" "FAIL" "matrix is missing one or more expected recursive capability IDs"
    fi
  else
    add_check "runtime_capability_matrix" "FAIL" "runtime capability matrix returned $cap_status"
  fi

  now_utc="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  event_id="recursive-e2e-$RUN_SUFFIX"
  jq -nc \
    --arg event_id "$event_id" \
    --arg tenant_id "$ADMIN_TENANT" \
    --arg trace_id "recursive-e2e-trace-$RUN_SUFFIX" \
    --arg timestamp_utc "$now_utc" \
    '{
      schema_version:"recursive-event.v1",
      event_id:$event_id,
      event_type:"response_quality_observation",
      tenant_id:$tenant_id,
      user_scope:{scope_type:"tenant"},
      source:{component:"recursive-e2e-sweep",source_type:"critic",source_id:$event_id},
      confidence:0.91,
      trace_id:$trace_id,
      policy_status:"observe_only",
      timestamp_utc:$timestamp_utc,
      temporal_state:"current",
      payload:{observation:"e2e recursive validation"},
      provenance:{source_hash:"0123456789abcdef0123456789abcdef",created_utc:$timestamp_utc,actor:"recursive-e2e-sweep"}
    }' > "$RUN_DIR/recursive-event.json"

  recursive_post_status="$(curl -sS -o "$RUN_DIR/recursive-event-response.json" -w '%{http_code}' \
    "$ORCHESTRATOR_BASE_URL/api/recursive/events" \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H 'Content-Type: application/json' \
    --data-binary @"$RUN_DIR/recursive-event.json" || true)"
  if [[ "$recursive_post_status" == "201" || "$recursive_post_status" == "202" ]]; then
    add_check "recursive_event_ingest" "PASS" "recursive event ingest returned $recursive_post_status"
  else
    add_check "recursive_event_ingest" "FAIL" "recursive event ingest returned $recursive_post_status"
  fi

  recursive_query_status="$(curl -sS -o "$RUN_DIR/recursive-event-query.json" -w '%{http_code}' \
    "$ORCHESTRATOR_BASE_URL/api/recursive/events?tenant_id=$(printf '%s' "$ADMIN_TENANT" | jq -sRr @uri)&event_type=response_quality_observation&page_size=10" \
    -H "Authorization: Bearer $ADMIN_TOKEN" || true)"
  if [[ "$recursive_query_status" == "200" ]] && jq -e --arg event_id "$event_id" '.[]? | select(.event_id == $event_id or .eventId == $event_id)' "$RUN_DIR/recursive-event-query.json" >/dev/null 2>&1; then
    add_check "recursive_event_query" "PASS" "recursive event query returned ingested event"
  else
    add_check "recursive_event_query" "FAIL" "recursive event query returned $recursive_query_status or did not include event"
  fi
fi

critic_health_status="$(curl -sS -o "$RUN_DIR/critic-health.json" -w '%{http_code}' "$CRITIC_BASE_URL/health" || true)"
if [[ "$critic_health_status" == "200" ]]; then
  add_check "critic_node_health" "PASS" "Critic node health returned 200"
else
  add_check "critic_node_health" "FAIL" "Critic node health returned $critic_health_status"
fi

jq -nc '{
  schema_version:"runtime-critic-observation.v1",
  tenant_id:"recursive-e2e",
  user_id_hash:"recursive-e2e-user-hash",
  request_id:"recursive-e2e-critic",
  trace_id:"recursive-e2e-critic",
  context:{
    route:"model_answer",
    intent_type:"practical_guidance",
    query_classification:"general_reasoning",
    presentation_template_family:"practical_guidance",
    chat_response_profile:"chat_practical_guidance",
    evidence_required:false,
    trusted_web_context_requested:false,
    trusted_web_context_enabled:false,
    home_node_id_present:false,
    counts:{
      memory_results:0,
      source_results:0,
      graph_evidence:0,
      retrieved:0,
      home_node_sources:0,
      trusted_web_results:0
    }
  },
  response:{
    answer:"I do not have enough governed evidence to answer this everyday question.",
    confidence:0.5,
    execution_state:"completed",
    execution_target:"shared_compute",
    source_count:0,
    trace:{
      route:"model_answer",
      policy_mode:"observe",
      model:"recursive-e2e",
      pack_hash:"recursive-e2e",
      trace_id:"recursive-e2e-critic"
    }
  }
}' > "$RUN_DIR/critic-observe-request.json"

critic_observe_status="$(curl -sS -o "$RUN_DIR/critic-observe-response.json" -w '%{http_code}' \
  "$CRITIC_BASE_URL/runtime/critic/observe" \
  -H 'Content-Type: application/json' \
  --data-binary @"$RUN_DIR/critic-observe-request.json" || true)"
if [[ "$critic_observe_status" == "200" ]]; then
  add_check "critic_observe_endpoint" "PASS" "Critic observe endpoint returned 200"
  if jq -e '.mode == "observe_only" and ([.findings[]?.rule] | index("backend_language_in_everyday_answer")) != null' "$RUN_DIR/critic-observe-response.json" >/dev/null 2>&1; then
    add_check "critic_structured_findings" "PASS" "Critic returned expected observe-only structured warning"
  else
    add_check "critic_structured_findings" "FAIL" "Critic response did not include expected structured warning"
  fi
else
  add_check "critic_observe_endpoint" "FAIL" "Critic observe endpoint returned $critic_observe_status"
fi

write_report() {
  local overall
  local failed_count
  overall="$(jq -r '.overall' "$SUMMARY_PATH")"
  failed_count="$(jq -r '.failed_count' "$SUMMARY_PATH")"

  {
    echo "# Recursive Intelligence E2E Sweep"
    echo
    echo "- Run ID: \`$RUN_ID\`"
    echo "- Result: \`$overall\`"
    echo "- Failed checks: \`$failed_count\`"
    echo "- Public ASK path: configured"
    echo "- Internal orchestrator path: configured"
    echo "- Critic path: configured"
    echo
    echo "## Checks"
    echo
    echo "| Check | Result | Detail |"
    echo "|---|---:|---|"
    jq -r '.checks[] | "| \(.name) | \(.status) | \(.detail | gsub("\\|"; "/")) |"' "$SUMMARY_PATH"
  } > "$REPORT_PATH"
}

jq -s --arg run_id "$RUN_ID" --arg report_path "$REPORT_PATH" --arg summary_path "$SUMMARY_PATH" '
  {
    schema_version:"cwi.recursive_e2e_sweep.v1",
    run_id:$run_id,
    overall:(if all(.status == "PASS") then "PASS" else "FAIL" end),
    failed_count:([.[] | select(.status != "PASS")] | length),
    summary_path:$summary_path,
    report_path:$report_path,
    checks:.
  }
' "$CHECKS_PATH" > "$SUMMARY_PATH"

write_report
cat "$SUMMARY_PATH"
