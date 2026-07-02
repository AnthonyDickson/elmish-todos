# TODO

- Review changes for:
  - [Security](#security-feedback)
  - Code style and organisation
  - Document prod setup
- Swap out todo store with SQLite database (Fumble)
- Add favico and setup static file serving
- Decide how to handle API errors after optimistic updates
- Create Docker image for release deployment

## Security Feedback

### Medium severity

#### 6. Client doesn't roll back optimistic updates on 401

TodoPage.fs optimistically mutates local state before calling the API. On a 401, it redirects
to /login with the stale optimistic state still in the model. Not a security hole per se, but
it means unauthenticated writes are momentarily reflected in the UI.
