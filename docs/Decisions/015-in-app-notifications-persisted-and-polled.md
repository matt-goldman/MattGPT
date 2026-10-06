## ADR-015: In-app notifications are persisted per user and polled by the web app

**Date:** 2026-10-06
**Status:** Accepted
**Related Issues:** [048 — Export conversation summary and in-app notifications](../Backlog/Done/048-export-summary-and-notifications.md)

### Context

Background work (imports, embedding runs, on-demand records and digests) finishes in the API service's hosted services. Before this, the only way to learn that it had finished was to sit on a page that polled a job-status endpoint. Notifications must survive page reloads and reach a user who wasn't looking when the work finished.

The Blazor Server web app is a separate process from the API and talks to it over HTTP through `MattGPT.ApiClient`. An in-process event bus pushed through the Blazor circuit (one of the options considered in issue 048) therefore can't see events raised in the API.

### Decision

- Notifications are **stored** behind `INotificationRepository` in Contracts, with MongoDB (`notifications` collection) and Postgres (`notifications` table, plain columns) implementations, like the other stores. Each one carries the recipient's `UserId` (null for the anonymous user when auth is off), a kind, title, message, optional app link, time and read flag.
- Background services raise them through `NotificationPublisher`, which never throws, so a notification failure can't fail the work it reports on.
- The web app **polls** `GET /notifications` every 5 seconds from a `NotificationBell` component in the nav bar. It shows a badge with the unread count, a list (opening it marks the shown items read via `POST /notifications/read`), and a toast for anything new since the page loaded.

### Consequences

- No new infrastructure: no SignalR hub, no SSE endpoint, no message broker. Delivery latency is up to the poll interval. For work that takes minutes to hours, 5 seconds is prompt.
- Each open circuit makes one small request every 5 seconds. That's fine for a single-user or small deployment. If it ever matters, the endpoint can grow a `since` parameter or move to SSE without changing the store.
- The MAUI app can use the same endpoints later.

### Alternatives Considered

- **SSE from the API to the web server, relayed into the circuit.** This means a long-lived connection per circuit through the service-to-service auth path, plus reconnect handling, to save a few seconds of latency.
- **SignalR hub in the API.** The same relay problem, plus a new dependency.
- **Notifications in memory only.** They would not survive an API restart, which fails the "persist across reloads" requirement for work that finishes while the user is away.
