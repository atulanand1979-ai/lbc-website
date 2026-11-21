// Development-only fake checkout (see dev/fake-pay.njk).
import { api, money, esc } from "./api.js";
const q = new URLSearchParams(location.search);
const kind = q.get("kind"), reference = q.get("ref"), amountPence = Number(q.get("amount"));
const isSafe = (u) => { try { return new URL(u, location.origin).origin === location.origin; } catch { return false; } };
document.getElementById("fp-desc").textContent = q.get("desc") || "";
document.getElementById("fp-amount").textContent = money(amountPence) + (kind === "recurring" ? " /month" : "");
const buttons = kind === "recurring"
  ? [["start", "Pay & start membership"], ["renew", "Simulate monthly renewal"], ["fail", "Simulate failed payment"], ["cancel", "Simulate cancellation at provider"]]
  : [["pay", "Pay now"]];
const box = document.getElementById("fp-buttons");
box.innerHTML = buttons.map(([o, l], i) => `<button class="btn ${i === 0 ? "btn--yellow-outline" : ""}" data-o="${o}">${esc(l)}</button>`).join("")
  + `<a class="btn" href="${esc(isSafe(q.get("cancel")) ? q.get("cancel") : "/")}">Cancel</a>`;
box.addEventListener("click", async (e) => {
  const o = e.target.dataset.o;
  if (!o) return;
  const msg = document.getElementById("fp-msg");
  try {
    const r = await api("dev/fake-pay", { method: "POST", body: { reference, kind, amountPence, outcome: o } });
    if (o === "pay" || o === "start") { const s = q.get("success"); location.assign(isSafe(s) ? s : "/"); return; }
    msg.className = "alert alert--success"; msg.textContent = `Sent ${r.type} event.`; msg.hidden = false;
  } catch (err) { msg.className = "alert alert--error"; msg.textContent = err.message; msg.hidden = false; }
});
