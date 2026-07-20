@external(javascript, "./test_setup_ffi.mjs", "setup")
pub fn setup() -> Nil

@external(javascript, "./test_setup_ffi.mjs", "patchLocation")
pub fn patch_location() -> Nil

@external(javascript, "./test_setup_ffi.mjs", "patchLocalStorage")
pub fn patch_local_storage() -> Nil

// Silence io.println_error (maps to console.error) during tests.
// Tests that verify error-handling inspect the Effect structure and
// model state, not the log output.
@external(javascript, "./test_setup_ffi.mjs", "patchPrintError")
pub fn patch_print_error() -> Nil

@external(javascript, "./test_setup_ffi.mjs", "patchFetch")
pub fn patch_fetch() -> Nil
