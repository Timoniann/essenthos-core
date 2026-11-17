// Load test of the read API, shaped like production traffic (k6, https://k6.io).
//
// One iteration is one page view as the reader's script makes it: a chapter (panes, context,
// strength), a Strong page (entry, books, the marked verse list), a record page
// (entity, references, kings, places) or an event page (event list window and step). The weights
// follow the prod access logs of 2026-11-14..17, where Applebot rendering the SPA is ~46% of requests.
//
//   k6 run scripts/load/essenthos.js                                  # local core, 10 views/s, 2 min
//   k6 run -e BASE=http://localhost:5279 -e RATE=20 -e DURATION=5m scripts/load/essenthos.js
//   k6 run -e PEAK=300 -e DURATION=6m scripts/load/essenthos.js        # ramp from RATE to PEAK views/s
//
// Registered as avioniq actions: load-local (a constant rate) and load-steps (a ramp to find where it
// breaks), both against the local core by default. Never against production without the owner's word:
// the rate limiter (300/min per address) answers 429 long before the server is loaded, and the run
// would compete with readers.
//
// BASE is the API origin (the paths start at /v1). RATE is page views per second, not requests:
// a view makes 3-5 requests. Thresholds abort the run when the API is clearly struggling, so a run
// against a live server stops itself instead of piling on.

import http from "k6/http"
import { check, group } from "k6"
import { Trend } from "k6/metrics"

const BASE = (__ENV.BASE || "http://localhost:5279").replace(/\/$/, "")
const RATE = Number(__ENV.RATE || 10)
const DURATION = __ENV.DURATION || "2m"
const LANGUAGES = [
  { language: "ukr", prose: "ukr", corpus: "UBIO" },
  { language: "eng", prose: "eng", corpus: "BSB" },
  { language: "eng", prose: "eng", corpus: "KJV" },
  { language: "deu", prose: "deu", corpus: "LUTH1912" },
]
// Common numbers (thousands of occurrences) and rare ones: the rare ones are the cold long tail.
const STRONG = [
  "H430", "H3068", "H559", "H1121", "H776", "H8064", "H1732", "H4428", "H5971", "H3478", "H419", "H5688",
  "G2316", "G2424", "G5547", "G4151", "G26", "G3056", "G2962", "G5348", "G3936", "G2599",
]
const PEAK = Number(__ENV.PEAK || 0)

// A 404 (a chapter a text does not hold, a number nobody renders) is an answer, not a failure.
http.setResponseCallback(http.expectedStatuses(200, 404))

export const options = {
  scenarios: {
    views: PEAK > 0
      ? {
          executor: "ramping-arrival-rate",
          startRate: RATE,
          timeUnit: "1s",
          stages: [{ target: PEAK, duration: DURATION }],
          preAllocatedVUs: Math.max(20, PEAK),
          maxVUs: Math.max(100, PEAK * 4),
        }
      : {
          executor: "constant-arrival-rate",
          rate: RATE,
          timeUnit: "1s",
          duration: DURATION,
          preAllocatedVUs: Math.max(10, RATE * 3),
          maxVUs: Math.max(50, RATE * 10),
        },
  },
  thresholds: {
    http_req_failed: [{ threshold: "rate<0.05", abortOnFail: true, delayAbortEval: "30s" }],
    http_req_duration: [{ threshold: "p(95)<3000", abortOnFail: true, delayAbortEval: "30s" }],
  },
  summaryTrendStats: ["avg", "med", "p(90)", "p(95)", "p(99)", "max", "count"],
}

const views = {
  chapter: new Trend("view_chapter", true),
  strong: new Trend("view_strong", true),
  record: new Trend("view_record", true),
  event: new Trend("view_event", true),
}

function get(path, name) {
  const response = http.get(`${BASE}${path}`, { tags: { name }, headers: { Accept: "application/json" } })
  check(response, { [`${name} ok`]: (r) => r.status === 200 || r.status === 404 })
  return response
}

function json(response) {
  try {
    return response.json()
  } catch {
    return null
  }
}

const pick = (list) => list[Math.floor(Math.random() * list.length)]

export function setup() {
  const books = json(http.get(`${BASE}/v1/books`))?.items ?? []
  const entities = [0, 500, 1000, 1500].flatMap(
    (skip) => json(http.get(`${BASE}/v1/entities?take=500&skip=${skip}&sort=verses`))?.items ?? []
  )
  const events = json(http.get(`${BASE}/v1/events?take=500`))?.items ?? []
  if (books.length === 0 || entities.length === 0) {
    throw new Error(`The API at ${BASE} gave no books or records; is it running?`)
  }
  return {
    books: books.filter((b) => b.chapterCount > 0).map((b) => ({ slug: b.slug, chapters: b.chapterCount })),
    entities: entities.map((e) => e.slug),
    events: events.map((e) => e.slug),
  }
}

function chapter(seed, reader) {
  const book = pick(seed.books)
  const n = 1 + Math.floor(Math.random() * book.chapters)
  get(`/v1/panes/${reader.corpus}/${book.slug}/${n}`, "panes")
  get(`/v1/context/${book.slug}/${n}?language=${reader.language}&prose=${reader.prose}`, "context")
  get(`/v1/strength/${book.slug}/${n}?texts=${reader.corpus}`, "strength")
}

function randomStrong() {
  return Math.random() < 0.6 ? `H${1 + Math.floor(Math.random() * 8674)}` : `G${1 + Math.floor(Math.random() * 5624)}`
}

function strong(seed, reader) {
  const number = Math.random() < 0.5 ? pick(STRONG) : randomStrong()
  get(`/v1/strong/${number}?language=${reader.prose}`, "strong")
  get(`/v1/strong/${number}/books?corpus=${reader.corpus}`, "strong-books")
  const page = json(get(`/v1/strong/${number}/verses?corpus=${reader.corpus}&skip=0&take=40`, "strong-verses"))
  const refs = (page?.items ?? [])
    .map((v) => v.book?.slug && `${v.book.slug}:${v.chapter}:${v.verse}`)
    .filter(Boolean)
  if (refs.length > 0) {
    get(`/v1/verses?corpus=${reader.corpus}&verses=${refs.join(",")}&strong=${number}`, "verses-marked")
  }
}

function record(seed, reader) {
  const slug = encodeURIComponent(pick(seed.entities))
  get(`/v1/entities/${slug}?language=${reader.language}&prose=${reader.prose}`, "entity")
  get(`/v1/entities/${slug}/references?skip=0&take=50`, "entity-references")
  get(`/v1/timeline/kings?language=${reader.language}`, "kings")
  get(`/v1/entities/${slug}/places?language=${reader.language}`, "entity-places")
}

function event(seed, reader) {
  if (seed.events.length === 0) return
  const slug = encodeURIComponent(pick(seed.events))
  get(`/v1/events/${slug}?language=${reader.language}&prose=${reader.prose}`, "event")
  get(`/v1/events/${slug}/step?language=${reader.language}&prose=${reader.prose}`, "event-step")
  get(`/v1/events?around=${slug}&take=9&language=${reader.language}&prose=${reader.prose}`, "events-near")
}

// Shares of page views, from the prod request mix (context/panes ~ chapters, strong, entities, events).
const MIX = [
  [0.4, "chapter", chapter],
  [0.25, "strong", strong],
  [0.2, "record", record],
  [0.15, "event", event],
]

export default function (seed) {
  const reader = pick(LANGUAGES)
  let roll = Math.random()
  for (const [share, name, view] of MIX) {
    roll -= share
    if (roll <= 0 || name === "event") {
      const started = Date.now()
      group(name, () => view(seed, reader))
      views[name].add(Date.now() - started)
      return
    }
  }
}
