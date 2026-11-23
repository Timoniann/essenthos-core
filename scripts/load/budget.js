// The SQL statement budget of the routes readers call most, checked against a running API (k6).
//
// Each route is asked once (after one warm-up ask, so a first-start read is not counted) and the
// statements the API ran for it are read from its Server-Timing header (RequestTiming:
// db;desc="N statements"). A route over its budget fails the run. The budgets are today's counts on
// the full corpus (2026-11-17, release 20261117b): a change that makes a route run more statements is
// a regression to explain, or a budget to raise here on purpose, in the same commit.
//
//   k6 run scripts/load/budget.js                              # the local core
//   k6 run -e BASE=http://localhost:5289 scripts/load/budget.js # an agent's private API
//
// Registered as the avioniq action load-budget. It reads only; it needs the API to send Server-Timing,
// which every environment does unless RequestTiming is switched off.

import http from "k6/http"
import { check } from "k6"
import { Counter } from "k6/metrics"

const BASE = (__ENV.BASE || "http://localhost:5279").replace(/\/$/, "")

// [name, path, most statements]
const ROUTES = [
  ["panes", "/v1/panes/BSB/genesis/1", 1],
  ["context", "/v1/context/genesis/1?language=eng&prose=eng", 4],
  ["context-ukr", "/v1/context/matthew/1?language=ukr&prose=ukr", 3],
  ["strength", "/v1/strength/genesis/1?texts=BSB", 1],
  ["strong", "/v1/strong/H430?language=eng", 7], // the counterparts in the other language are one read of strong_counterpart
  ["strong-books", "/v1/strong/H430/books?corpus=BSB", 8],
  ["strong-verses", "/v1/strong/H430/verses?corpus=BSB&skip=0&take=40", 5],
  ["strong-occurrences", "/v1/strong/G2316/occurrences?skip=40&take=20", 4],
  ["strong-renderings", "/v1/strong/G2316/renderings?corpus=KJV", 4],
  ["verses-marked", "/v1/verses?corpus=BSB&verses=genesis:1:1,genesis:1:2&strong=H430", 4], // 8 until TSK-1822 (one read of word_rendered_strong)
  ["entity", "/v1/entities/david?language=eng&prose=eng", 1],
  ["entity-references", "/v1/entities/david/references?skip=0&take=50", 6],
  ["entity-places", "/v1/entities/david/places?language=eng", 7],
  ["kings", "/v1/timeline/kings?language=eng", 16],
  ["event", "/v1/events/beginjehu2reign?language=eng&prose=eng", 3],
  ["event-step", "/v1/events/beginjehu2reign/step?language=eng&prose=eng", 4],
  ["events-near", "/v1/events?around=beginjehu2reign&take=9&language=eng&prose=eng", 4],
]

const over = new Counter("routes_over_budget")
const unread = new Counter("routes_unread")

export const options = {
  vus: 1,
  iterations: 1,
  thresholds: {
    routes_over_budget: ["count==0"],
    routes_unread: ["count==0"],
  },
  summaryTrendStats: ["med", "max"],
}

export default function () {
  for (const [name, path, budget] of ROUTES) {
    http.get(`${BASE}${path}`, { tags: { name: `${name}-warm` } })
    const response = http.get(`${BASE}${path}`, { tags: { name } })
    const timing = response.headers["Server-Timing"] || ""
    const said = /desc="(\d+) statements"/.exec(timing)
    if (response.status !== 200 || !said) {
      unread.add(1)
      console.error(`${name}: status ${response.status}, Server-Timing "${timing}"`)
      continue
    }
    const statements = Number(said[1])
    const ok = check(statements, { [`${name} within ${budget}`]: (n) => n <= budget })
    if (!ok) over.add(1)
    console.log(`${ok ? "ok  " : "OVER"} ${name.padEnd(20)} ${String(statements).padStart(3)} of ${budget}`)
  }
}
