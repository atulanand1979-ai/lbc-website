// Small client for the C# API (same origin under /api, auth via httpOnly cookie).

export class ApiError extends Error {
  constructor(message, status) { super(message); this.status = status; }
}

export async function api(path, { method = "GET", body } = {}) {
  const res = await fetch(`/api/${path}`, {
    method,
    credentials: "same-origin",
    headers: body === undefined ? {} : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  let data = null;
  const text = await res.text();
  if (text) { try { data = JSON.parse(text); } catch { /* non-JSON error page */ } }
  if (!res.ok) throw new ApiError(data?.error || "Sorry, something went wrong. Please try again.", res.status);
  return data;
}

let configPromise;
export function getConfig() {
  configPromise ??= api("config").catch(() => ({ prices: null, captchaSiteKey: null }));
  return configPromise;
}

export const money = (pence) => pence % 100 === 0 ? `£${pence / 100}` : `£${(pence / 100).toFixed(2)}`;

export const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

export function formData(form) {
  return Object.fromEntries([...new FormData(form).entries()].map(([k, v]) => [k, typeof v === "string" ? v.trim() : v]));
}

export function showMessage(el, kind, message) {
  if (!el) return;
  el.className = `alert alert--${kind}`;
  el.textContent = message;
  el.hidden = false;
  el.setAttribute("role", kind === "error" ? "alert" : "status");
  el.scrollIntoView({ behavior: "smooth", block: "nearest" });
}

/** Wraps a form submit: disables the button, shows API errors in the form's [data-message] element. */
export function handleSubmit(form, handler) {
  const message = form.querySelector("[data-message]");
  const button = form.querySelector("button[type=submit]");
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    if (message) message.hidden = true;
    if (button) button.disabled = true;
    try {
      await handler(formData(form));
    } catch (err) {
      showMessage(message, "error", err.message);
    } finally {
      if (button) button.disabled = false;
    }
  });
}

// ---- Cloudflare Turnstile (only when the API reports a site key) ----
let turnstileLoading;
export async function mountCaptcha(form) {
  const slot = form.querySelector("[data-captcha]");
  if (!slot) return () => null;
  const { captchaSiteKey } = await getConfig();
  if (!captchaSiteKey) return () => null;
  turnstileLoading ??= new Promise((resolve, reject) => {
    const s = document.createElement("script");
    s.src = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";
    s.async = true;
    s.onload = resolve;
    s.onerror = reject;
    document.head.appendChild(s);
  });
  await turnstileLoading;
  const id = window.turnstile.render(slot, { sitekey: captchaSiteKey });
  return () => {
    const token = window.turnstile.getResponse(id);
    window.turnstile.reset(id);
    return token;
  };
}
