// Demo Direct Debit page (see dev/fake-direct-debit.njk). Bank details are only format-checked here
// and are never sent: completing the page just tells our API "the Direct Debit was set up".
import { api, money } from "./api.js";

const q = new URLSearchParams(location.search);
const reference = q.get("ref"), amountPence = Number(q.get("amount"));
const isSafe = (u) => { try { return new URL(u, location.origin).origin === location.origin; } catch { return false; } };
const safe = (u) => (u && isSafe(u) ? u : "/");
const $ = (id) => document.getElementById(id);

$("dd-desc").textContent = q.get("desc") || "Membership";
$("dd-amount").textContent = money(amountPence);
$("dd-cancel").href = safe(q.get("cancel"));

const form = $("dd-details");
const [given, ...rest] = (q.get("name") || "").trim().split(/\s+/);
const family = rest.pop() || "";
form.given.value = [given, ...rest].filter(Boolean).join(" ");
form.family.value = family;
form.email.value = q.get("email") || "";
form.holder.value = (q.get("name") || "").trim();

const digits = (s) => s.replace(/\D/g, "");
const checks = {
  given: (v) => v.trim() !== "" || "Enter your first name.",
  family: (v) => v.trim() !== "" || "Enter your last name.",
  email: (v) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v.trim()) || "Enter a valid email address.",
  postcode: (v) => /^[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2}$/i.test(v.trim()) || "Enter a valid UK postcode.",
  holder: (v) => v.trim() !== "" || "Enter the account holder's name.",
  sort: (v) => digits(v).length === 6 || "Sort code must be 6 digits.",
  account: (v) => digits(v).length === 8 || "Account number must be 8 digits.",
};

form.addEventListener("submit", (e) => {
  e.preventDefault();
  const errors = [];
  for (const [name, check] of Object.entries(checks)) {
    const ok = check(form[name].value);
    form[name].setAttribute("aria-invalid", ok === true ? "false" : "true");
    if (ok !== true) errors.push(ok);
  }
  if (!form.sole.checked) errors.push("Please confirm you are the account holder.");
  const box = $("dd-error");
  if (errors.length) { box.textContent = errors[0]; box.hidden = false; return; }
  box.hidden = true;

  const s = digits(form.sort.value);
  const rows = [
    ["Name", `${form.given.value.trim()} ${form.family.value.trim()}`],
    ["Email", form.email.value.trim()],
    ["Account holder", form.holder.value.trim()],
    ["Sort code", `${s.slice(0, 2)}-${s.slice(2, 4)}-${s.slice(4)}`],
    ["Account number", `••••${digits(form.account.value).slice(-4)}`],
    ["Amount", `${money(amountPence)} every month`],
  ];
  const dl = $("dd-summary");
  dl.replaceChildren(...rows.flatMap(([k, v]) => {
    const dt = document.createElement("dt"); dt.textContent = k;
    const dd = document.createElement("dd"); dd.textContent = v;
    return [dt, dd];
  }));
  form.hidden = true;
  $("dd-confirm").hidden = false;
  window.scrollTo(0, 0);
});

$("dd-back").addEventListener("click", () => { $("dd-confirm").hidden = true; form.hidden = false; });

$("dd-submit").addEventListener("click", async (e) => {
  const btn = e.currentTarget;
  btn.disabled = true; btn.textContent = "Setting up…";
  try {
    await api("dev/fake-pay", { method: "POST", body: { reference, kind: "recurring", amountPence, outcome: "start" } });
    location.assign(safe(q.get("success")));
  } catch (err) {
    btn.disabled = false; btn.textContent = "Set up Direct Debit";
    const box = $("dd-error"); box.textContent = err.message;
    $("dd-confirm").hidden = true; form.hidden = false; box.hidden = false;
  }
});

document.querySelector(".dd__tools-row").addEventListener("click", async (e) => {
  const o = e.target.dataset?.o;
  if (!o) return;
  const msg = $("dd-tool-msg");
  try {
    const r = await api("dev/fake-pay", { method: "POST", body: { reference, kind: "recurring", amountPence, outcome: o } });
    msg.textContent = `Sent ${r.type} event.`;
  } catch (err) { msg.textContent = err.message; }
  msg.hidden = false;
});
