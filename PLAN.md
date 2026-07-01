# TODO

- Review changes for:
  - [Security](#security-feedback)
  - Code style and organisation
- Swap out todo store with SQLite database (Fumble)
- Add favico and setup static file serving
- Create Docker image for release deployment

## Security Feedback

### Medium severity

#### 4. Logout doesn't terminate the OIDC session

Program.fs:159-162 only calls SignOutAsync on the cookie scheme — it never signs out of
Authelia. If the user's Authelia session is still alive, re-visiting the app will auto-log them
in without any prompt. Add an OIDC sign-out callback or redirect to Authelia's /api/oidc/end-
session endpoint.

#### 5. No explicit cookie security defaults

The cookie auth handler relies entirely on framework defaults. For production, explicitly set:

• Cookie.SecurePolicy = CookieSecurePolicy.Always
• Cookie.SameSite = SameSiteMode.Lax (or Strict )
• Cookie.HttpOnly = true
• Cookie.IsEssential = true
• ExpireTimeSpan and SlidingExpiration tuned to your needs

#### 6. Client doesn't roll back optimistic updates on 401

TodoPage.fs optimistically mutates local state before calling the API. On a 401, it redirects
to /login with the stale optimistic state still in the model. Not a security hole per se, but
it means unauthenticated writes are momentarily reflected in the UI.

---

### Low / informational

• Hardcoded OAuth2 URLs in the OpenAPI doc transformer ( Program.fs:101-102 point to 127.0.0.
1:9091 ) — but they're behind IsDevelopment() , so fine.
• No CORS configuration — fine as long as the SPA and API are same-origin. If they ever diverge,
this needs explicit configuration.
• Authelia CORS allowed_origins: '*' ( configuration.yml:98 ) — overly permissive for prod;
restrict to the actual allowed origins.
• SaveTokens = true stores access/refresh tokens in the encrypted auth cookie — standard and
fine.
