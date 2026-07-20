// Polyfill browser APIs for gleeunit (Node.js) tests.

export function patchLocation() {
  globalThis.location = { origin: "http://localhost" };

  globalThis.window = {
    location: globalThis.location,
  };
}

export function patchLocalStorage() {
  let storage = {};

  globalThis.window = {
    localStorage: {
      getItem(key) {
        return storage[key] ?? null;
      },
      setItem(key, value) {
        storage[key] = value;
      },
    },
  };
}

export function patchPrintError() {
  // Silence io.println_error (maps to console.error) during tests.
  // Tests that verify error-handling inspect the Effect structure and
  // model state, not the log output.
  globalThis.console = {
    ...globalThis.console,
    error: () => {},
  };

}

export function patchFetch() {
  // fetch stub: never resolves. Effect constructors only build the
  // promise; tests inspect the effect structure, not HTTP responses.
  globalThis.fetch = () =>
    new Promise(() => {});
}

export function setup() {
  patchLocation();
  patchLocalStorage();
  patchStdErr();
  patchFetch();
}
