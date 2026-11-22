// Member account: sign in, then show membership and Pay As You Go bookings.
import { api, ApiError, esc, handleSubmit, money } from "./api.js";

const loginPanel = document.getElementById("login-panel");
const accountPanel = document.getElementById("account-panel");
const loginForm = document.getElementById("login-form");

const fmtDate = (iso) => new Date(iso).toLocaleDateString("en-GB", { weekday: "short", day: "numeric", month: "long", year: "numeric" });
const STATUS = { Active: "Active", PastDue: "Payment overdue", Cancelled: "Cancelled" };

function bookingRows(list) {
  if (!list.length) return `<p class="muted">None.</p>`;
  return `<div class="table-wrap"><table class="data"><thead><tr><th>Date</th><th>Class</th><th>Price</th></tr></thead><tbody>
    ${list.map((b) => `<tr><td>${esc(fmtDate(b.date))}</td><td>${esc(b.classLabel)}</td><td>${money(b.amountPence)}${b.status === "PayAtClass" ? ` <span class="small muted">pay at class</span>` : ` <span class="small muted">paid</span>`}</td></tr>`).join("")}
  </tbody></table></div>`;
}

function render(a) {
  const m = a.membership;
  const membership = m
    ? `<div class="card card--feature">
        <span class="eyebrow">Membership</span>
        <h3>${esc(m.tier)}</h3>
        <p><span class="status status--${esc(m.status)}">${esc(STATUS[m.status] || m.status)}</span></p>
        <p>${money(m.monthlyPricePence)} per month${m.tier === "Bronze" && m.homeVenueCity ? ` · Home location: <b>${esc(m.homeVenueCity)}</b>` : ""}</p>
        ${m.currentPeriodEnd && m.status !== "Cancelled" ? `<p class="muted">Next payment due: ${esc(fmtDate(m.currentPeriodEnd))}</p>` : ""}
        <p class="muted small">Members don't need to book — just turn up and give your name to the teacher. To change or cancel your membership, please contact us.</p>
      </div>`
    : `<div class="card"><h3>No membership</h3><p>Save with a monthly membership — unlimited classes from £25 a month.</p><div class="btn-row"><a class="btn btn--yellow-outline" href="/signup/#classes">View memberships</a></div></div>`;

  accountPanel.innerHTML = `
    <div class="section__head section__head--split">
      <div><span class="eyebrow">My account</span><h2>Hi, ${esc(a.name.split(" ")[0])}</h2></div>
      <div class="btn-row">
        ${a.role === "Admin" ? `<a class="btn btn--black" href="/admin/">Admin area</a>` : ""}
        <a class="btn btn--yellow-outline" href="/book/">Book a class</a>
        <button class="btn" type="button" id="logout">Sign out</button>
      </div>
    </div>
    <div class="grid grid--wide">${membership}
      <div class="card"><h3>Your details</h3><p>${esc(a.name)}<br>${esc(a.email)}<br>${esc(a.phone)}</p></div>
    </div>
    <h3>Upcoming classes</h3>${bookingRows(a.upcoming)}
    <h3>Past classes</h3>${bookingRows(a.past)}`;
  document.getElementById("logout").addEventListener("click", async () => {
    await api("auth/logout", { method: "POST", body: {} });
    // Make sure the session really ended rather than silently reloading as the same user.
    const stillSignedIn = await api("me").then(() => true, () => false);
    if (stillSignedIn) { alert("Sorry, we couldn't sign you out. Please close your browser to end the session."); return; }
    location.reload();
  });
}

async function load() {
  try {
    const account = await api("me");
    loginPanel.hidden = true;
    accountPanel.hidden = false;
    render(account);
  } catch (err) {
    if (!(err instanceof ApiError && err.status === 401)) throw err;
    accountPanel.hidden = true;
    loginPanel.hidden = false;
  }
}

handleSubmit(loginForm, async (data) => {
  await api("auth/login", { method: "POST", body: data });
  const next = new URLSearchParams(location.search).get("next");
  if (next === "/admin/") location.assign(next); else await load();
});

load();
