export function loadFromStore(key) {
  const value = window.localStorage.getItem(key);
  return value ?? "";
}

export function saveToStore(key, value) {
  window.localStorage.setItem(key, value);
}

export function redirect(url) {
  window.location.assign(url);
}
