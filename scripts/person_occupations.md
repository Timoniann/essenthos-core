# Person occupations: TSK-1250

The owner authorized preparing and launching Sonnet 5.5 readings for all current persons with a new **$100 total estimated API budget**, including pilot, drafts, verifier calls and retries. This is generation for review, without occupation database/UI admission. Presence admission is separate and already served.

## Frozen run

`Resources/Essenthos/generation/person-occupations-sonnet55-2026-10-27-v1` contains the immutable current person/index export, source packets, policy, held-out pilot cases, code snapshots, request/response evidence and checkpoints. There are 3,176 persons, 27,821 indexed references and 1,393 jobs; 85 persons without indexed references remain explicit incomplete-source cases. This is all indexed references, not a claim that every unindexed textual mention has been discovered.

The source export includes BHSA OT, NESTLE1904 NT, BSB and primary reading context, with secondary canonical mappings and copied license notices. Existing name/role/author metadata is not profession evidence. Expected pilot answers never go into model prompts. Literal trades, offices, military and religious roles are distinct. Moral labels, status, one-off acts and metaphors remain excluded or review observations. The pending owner question about moral designations in the person line is not answered by this run.

Each finding requires source-bound exact quotations, identity evidence, a focus-chapter profession witness and an original-language witness when supplied. NFC equivalence is allowed; stripping marks, paraphrases and invented quotations are rejected. Positive drafts receive a second independent model reading. Empty drafts bypass a paid positive-fact verifier; absence of a finding is not proof of no profession.

## Operations

Use registered avioniq actions/services from the control folder. The managed service `person-occupations-sonnet55` executes the exact frozen code snapshot, not a mutable worktree. Its stop hook writes a stop request and cancels unfinished provider batches. `person-occupations` provides prepare/pilot/report/cancel operations; do not run a second paid controller alongside the managed service.

The shared main `scripts/claude_api.py` supplies the owner's key and usage ledger. Actual model must equal `claude-sonnet-5-5`; no subscription, model fallback or automatic SDK retries. Separate `person-occupations-TSK-1250-draft` and `-check` labels share one $100 cap. Drafts have a $70 subcap to preserve verifier budget. Pilot is capped at $10 within the same total. One provider batch is active at a time; reservation uses a conservative input/output upper bound before submitting. Unknown cost, unknown request outcome, changed source/code or exhausted retries fail closed.

Read `status.json`, `checkpoint.json`, raw/attempt/checked folders and `.claude-api/usage.jsonl` together. Submitting a batch is not completion. All message IDs, actual token usage and invalid attempts are retained. A validated empty result is distinguishable from missing output. The controller backs up after each batch collection and on completion and moves TSK-1250 to review, never human acceptance. Generation results never load themselves into core.

For restart, first inspect the saved checkpoint, live provider batch and controller PID. Do not remove a lock based only on its age, blindly resubmit an unknown-outcome request, clear cancellation markers or raise caps. A deliberate resume must preserve all actual spend, successful results and source hashes. Structural pilot failures can be retried by the same frozen pilot action after inspecting their saved raw answer, at most three attempts per job and within the existing cap.

## Reproduction and checks

The maintained preparation helper's `--main` must explicitly identify the main checkout when called outside its original worktree. Frozen verification requires the exact preparation source in `input-preparation-source` with that same explicit `--main`; Git newline normalization can change maintained source bytes. Never rewrite a paid run's manifest or script to make a changed checkout match.

Offline checks: `person-occupations-check` and `person-occupation-inputs-check`. Required core build and non-Corpus checks use isolated output through registered actions. No full corpus-coverage verification is part of this request. Paid pilot semantic review is recorded in `pilot-semantic-review.json`, bound to the run manifest and explicitly not human acceptance.
