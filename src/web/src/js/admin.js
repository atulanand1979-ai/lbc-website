// Admin area: places & weekly classes, class register, members, enquiries.
import { api, ApiError, esc, money } from "./api.js";

const root = document.getElementById("admin");
const denied = document.getElementById("admin-denied");
const DAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
const LEVELS = ["Beginners", "Intermediate"];
const fmtDate = (iso) => new Date(iso).toLocaleDateString("en-GB", { weekday: "short", day: "numeric", month: "short", year: "numeric" });
const todayIso = () => new Date().toLocaleDateString("en-CA"); // yyyy-mm-dd in local time

let venues = [];

// ---------- tabs ----------
const tabs = [...root.querySelectorAll("[role=tab]")];
function selectTab(name) {
  for (const t of tabs) {
    const on = t.dataset.tab === name;
    t.setAttribute("aria-selected", String(on));
    document.getElementById(`tab-${t.dataset.tab}`).hidden = !on;
  }
  history.replaceState(null, "", `#${name}`);
  ({ places: loadPlaces, register: initRegister, members: loadMembers, enquiries: loadEnquiries })[name]?.();
}
tabs.forEach((t) => t.addEventListener("click", () => selectTab(t.dataset.tab)));

// ---------- dialogs ----------
const dialog = document.getElementById("admin-dialog");
function openDialog(title, bodyHtml, onSubmit) {
  dialog.innerHTML = `<form method="dialog" class="form" novalidate>
    <h2 class="dialog__title">${esc(title)}</h2>${bodyHtml}
    <div class="alert alert--error" data-message hidden></div>
    <div class="btn-row"><button class="btn btn--yellow-outline" type="submit">Save</button><button class="btn" type="button" data-close>Cancel</button></div>
  </form>`;
  const form = dialog.querySelector("form");
  const msg = form.querySelector("[data-message]");
  form.querySelector("[data-close]").addEventListener("click", () => dialog.close());
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    msg.hidden = true;
    const btn = form.querySelector("button[type=submit]");
    btn.disabled = true;
    try { await onSubmit(form); dialog.close(); }
    catch (err) { msg.textContent = err.message; msg.hidden = false; }
    finally { btn.disabled = false; }
  });
  dialog.showModal();
  return form;
}

const field = (label, input, hint = "") =>
  `<div class="field"><label>${esc(label)}${input}</label>${hint ? `<span class="field__hint">${esc(hint)}</span>` : ""}</div>`;

// ---------- places & weekly classes ----------
async function loadPlaces() {
  const box = document.getElementById("places-list");
  box.innerHTML = `<p class="muted">Loading…</p>`;
  venues = await api("manage/venues");
  document.getElementById("seed-box").hidden = venues.length > 0;
  box.innerHTML = venues.map((v) => `
    <article class="admin-venue ${v.isActive ? "" : "admin-venue--inactive"}">
      <div class="admin-venue__head">
        <div><span class="admin-venue__title">${esc(v.city)}</span>
          ${v.isActive ? "" : `<span class="status status--Cancelled">Hidden</span>`}
          ${v.startsOn ? `<span class="status">Starts ${esc(fmtDate(v.startsOn))}</span>` : ""}
          <div class="muted small">${esc(v.name)}, ${esc(v.address)}, ${esc(v.postcode)}</div></div>
        <div class="btn-row">
          <button class="btn btn--small" data-edit-venue="${esc(v.slug)}">Edit place</button>
          <button class="btn btn--small btn--black" data-add-class="${esc(v.slug)}">+ Weekly class</button>
        </div>
      </div>
      ${v.classes.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>Every</th><th>Time</th><th>Level</th><th>Not running on</th><th></th></tr></thead><tbody>
        ${v.classes.map((c) => `<tr>
          <td><b>${esc(c.dayOfWeek)}</b>${c.isActive ? "" : ` <span class="status status--Cancelled">Off</span>`}</td>
          <td>${esc(c.startTime)}–${esc(c.endTime)}</td><td>${esc(c.level)}</td>
          <td class="small">${c.skipDates.length ? c.skipDates.map((d) => esc(fmtDate(d))).join("<br>") : "-"}</td>
          <td><div class="btn-row"><button class="btn btn--small" data-edit-class="${esc(v.slug)}|${esc(c.id)}">Edit</button>
            <button class="btn btn--small btn--danger" data-delete-class="${esc(v.slug)}|${esc(c.id)}">Delete</button></div></td>
        </tr>`).join("")}</tbody></table></div>` : `<p class="muted">No weekly classes yet. Add one so people can book.</p>`}
    </article>`).join("") || `<p class="muted">No places yet.</p>`;
}

function venueDialog(v) {
  const isNew = !v;
  v ??= { slug: "", city: "", name: "", address: "", postcode: "", mapUrl: "", isActive: true, startsOn: "", sortOrder: venues.length + 1 };
  openDialog(isNew ? "Add a place" : `Edit ${v.city}`, `
    <div class="form__row">${field("City / town", `<input name="city" required value="${esc(v.city)}">`)}
      ${isNew ? field("Web key", `<input name="slug" value="" placeholder="auto from city">`, "Lower-case, used in links. Can't be changed later.") : ""}</div>
    ${field("Venue name", `<input name="name" required value="${esc(v.name)}">`)}
    <div class="form__row">${field("Address", `<input name="address" value="${esc(v.address)}">`)}${field("Postcode", `<input name="postcode" value="${esc(v.postcode)}">`)}</div>
    ${field("Map link (optional)", `<input name="mapUrl" type="url" value="${esc(v.mapUrl || "")}" placeholder="https://maps.google.com/…">`)}
    <div class="form__row">${field("Classes start on (optional)", `<input name="startsOn" type="date" value="${esc(v.startsOn || "")}">`, "For a new place that hasn't launched yet.")}
      ${field("Order on site", `<input name="sortOrder" type="number" min="0" value="${esc(v.sortOrder)}">`)}</div>
    <label class="check"><input type="checkbox" name="isActive" ${v.isActive ? "checked" : ""}> Show on website and allow bookings</label>`,
    async (form) => {
      const f = form.elements;
      const body = { city: f.city.value, slug: f.slug?.value, name: f.name.value, address: f.address.value, postcode: f.postcode.value,
        mapUrl: f.mapUrl.value, startsOn: f.startsOn.value || null, sortOrder: Number(f.sortOrder.value || 0), isActive: f.isActive.checked };
      if (isNew) await api("manage/venues", { method: "POST", body });
      else await api(`manage/venues/${encodeURIComponent(v.slug)}`, { method: "PUT", body });
      await loadPlaces();
    });
}

function classDialog(venueSlug, c) {
  const v = venues.find((x) => x.slug === venueSlug);
  const isNew = !c;
  c ??= { dayOfWeek: "Monday", startTime: "19:00", endTime: "20:00", level: "Beginners", isActive: true, skipDates: [] };
  let skip = [...c.skipDates];
  const form = openDialog(isNew ? `New weekly class in ${v.city}` : `Edit ${v.city} class`, `
    <div class="form__row">${field("Every", `<select name="dayOfWeek">${DAYS.map((d) => `<option ${d === c.dayOfWeek ? "selected" : ""}>${d}</option>`).join("")}</select>`)}
      ${field("Level", `<select name="level">${LEVELS.map((l) => `<option ${l === c.level ? "selected" : ""}>${l}</option>`).join("")}</select>`)}</div>
    <div class="form__row">${field("Starts", `<input name="startTime" type="time" required value="${esc(c.startTime)}">`)}${field("Ends", `<input name="endTime" type="time" required value="${esc(c.endTime)}">`)}</div>
    <div class="field"><span class="label">Dates this class is NOT running (bank holidays, venue closed)</span>
      <div class="toolbar"><input type="date" data-skip-input><button class="btn btn--small" type="button" data-skip-add>Add date</button></div>
      <ul class="chips" data-skip-list></ul></div>
    <label class="check"><input type="checkbox" name="isActive" ${c.isActive ? "checked" : ""}> Running (offer for booking)</label>`,
    async (f) => {
      const body = { dayOfWeek: f.elements.dayOfWeek.value, startTime: f.elements.startTime.value, endTime: f.elements.endTime.value,
        level: f.elements.level.value, isActive: f.elements.isActive.checked, skipDates: skip };
      if (isNew) await api(`manage/venues/${encodeURIComponent(venueSlug)}/classes`, { method: "POST", body });
      else await api(`manage/venues/${encodeURIComponent(venueSlug)}/classes/${encodeURIComponent(c.id)}`, { method: "PUT", body });
      await loadPlaces();
    });
  const list = form.querySelector("[data-skip-list]");
  const renderSkip = () => {
    list.innerHTML = skip.sort().map((d) => `<li>${esc(fmtDate(d))} <button type="button" class="btn btn--small" data-skip-remove="${d}" aria-label="Remove ${esc(d)}">×</button></li>`).join("");
  };
  form.querySelector("[data-skip-add]").addEventListener("click", () => {
    const val = form.querySelector("[data-skip-input]").value;
    if (val && !skip.includes(val)) { skip.push(val); renderSkip(); }
  });
  list.addEventListener("click", (e) => {
    const d = e.target.dataset.skipRemove;
    if (d) { skip = skip.filter((x) => x !== d); renderSkip(); }
  });
  renderSkip();
}

document.getElementById("tab-places").addEventListener("click", async (e) => {
  const t = e.target.closest("button");
  if (!t) return;
  if (t.id === "add-venue") venueDialog();
  else if (t.id === "seed-venues") { await api("manage/seed", { method: "POST", body: {} }); await loadPlaces(); }
  else if (t.dataset.editVenue) venueDialog(venues.find((v) => v.slug === t.dataset.editVenue));
  else if (t.dataset.addClass) classDialog(t.dataset.addClass);
  else if (t.dataset.editClass) {
    const [slug, id] = t.dataset.editClass.split("|");
    classDialog(slug, venues.find((v) => v.slug === slug).classes.find((c) => c.id === id));
  } else if (t.dataset.deleteClass) {
    const [slug, id] = t.dataset.deleteClass.split("|");
    if (!confirm("Delete this weekly class? Existing bookings are kept. To pause it instead, edit it and untick 'Running'.")) return;
    await api(`manage/venues/${encodeURIComponent(slug)}/classes/${encodeURIComponent(id)}`, { method: "DELETE" });
    await loadPlaces();
  }
});

// ---------- register ----------
const regForm = document.getElementById("register-form");
async function initRegister() {
  if (!venues.length) venues = await api("manage/venues");
  const sel = regForm.elements.venue;
  if (!sel.options.length) sel.innerHTML = venues.map((v) => `<option value="${esc(v.slug)}">${esc(v.city)}</option>`).join("");
  if (!regForm.elements.date.value) regForm.elements.date.value = todayIso();
}
regForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  const out = document.getElementById("register-out");
  out.innerHTML = `<p class="muted">Loading…</p>`;
  try {
    const r = await api(`manage/register?venue=${encodeURIComponent(regForm.elements.venue.value)}&date=${regForm.elements.date.value}`);
    const classes = r.classes.length ? r.classes.map((c) => `
      <h3>${esc(c.class.label)}</h3>
      ${c.payAsYouGo.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>✓</th><th>Pay As You Go</th><th>Payment</th><th>Phone</th><th>Email</th></tr></thead><tbody>
        ${c.payAsYouGo.map((b) => `<tr><td>☐</td><td><b>${esc(b.name)}</b></td><td>${b.status === "PayAtClass" ? `<b>Collect ${money(b.amountPence)}</b>` : "Paid online"}</td><td>${esc(b.phone)}</td><td>${esc(b.email)}</td></tr>`).join("")}
      </tbody></table></div>` : `<p class="muted">No Pay As You Go bookings.</p>`}`).join("")
      : `<p class="alert alert--info">No classes run in ${esc(r.venueCity)} on that day of the week.</p>`;
    const members = r.members.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>✓</th><th>Member</th><th>Tier</th><th>Status</th><th>Phone</th></tr></thead><tbody>
      ${r.members.map((m) => `<tr><td>☐</td><td><b>${esc(m.name)}</b><br><span class="small muted">${esc(m.email)}</span></td><td>${esc(m.tier)}</td>
        <td><span class="status status--${esc(m.status)}">${esc(m.status)}</span></td><td>${esc(m.phone)}</td></tr>`).join("")}
      </tbody></table></div>` : `<p class="muted">No members for this location.</p>`;
    out.innerHTML = `<div class="section__head section__head--split"><div><h2>${esc(r.venueCity)} · ${esc(fmtDate(r.date))}</h2></div>
      <button class="btn" type="button" data-print>Print</button></div>
      ${classes}<h3>Members who can attend (Bronze ${esc(r.venueCity)}, Silver, Gold)</h3>${members}`;
  } catch (err) { out.innerHTML = `<p class="alert alert--error">${esc(err.message)}</p>`; }
});

document.getElementById("register-out").addEventListener("click", (e) => {
  if (e.target.closest("[data-print]")) window.print();
});

// ---------- members ----------
let memberships = [];
async function loadMembers() {
  if (!venues.length) venues = await api("manage/venues");
  memberships = await api("manage/memberships");
  renderMembers();
}
function renderMembers() {
  const f = document.getElementById("members-filter").elements;
  const q = f.q.value.trim().toLowerCase();
  const rows = memberships.filter((m) =>
    (!f.tier.value || m.tier === f.tier.value) && (!f.status.value || m.status === f.status.value) &&
    (!q || m.name.toLowerCase().includes(q) || m.email.includes(q)));
  const live = memberships.filter((m) => m.status === "Active" || m.status === "PastDue");
  document.getElementById("members-summary").textContent =
    `${live.length} live memberships · ${money(live.reduce((s, m) => s + m.monthlyPricePence, 0))} per month`;
  document.getElementById("members-out").innerHTML = rows.length ? `<div class="table-wrap"><table class="data">
    <thead><tr><th>Member</th><th>Tier</th><th>Home</th><th>Status</th><th>Next payment</th><th></th></tr></thead><tbody>
    ${rows.map((m) => `<tr><td><b>${esc(m.name)}</b><br><span class="small muted">${esc(m.email)} · ${esc(m.phone)}</span></td>
      <td>${esc(m.tier)}<br><span class="small muted">${money(m.monthlyPricePence)}/mo</span></td>
      <td>${esc(m.homeVenueCity || "-")}</td><td><span class="status status--${esc(m.status)}">${esc(m.status)}</span></td>
      <td class="small">${m.currentPeriodEnd && m.status !== "Cancelled" ? esc(fmtDate(m.currentPeriodEnd)) : "-"}</td>
      <td><div class="btn-row">${m.tier === "Bronze" && m.status !== "Cancelled" ? `<button class="btn btn--small" data-home="${esc(m.id)}">Change home</button>` : ""}
        ${m.status !== "Cancelled" ? `<button class="btn btn--small btn--danger" data-cancel="${esc(m.id)}">Cancel</button>` : ""}</div></td></tr>`).join("")}
    </tbody></table></div>` : `<p class="muted">No memberships match.</p>`;
}
document.getElementById("members-filter").addEventListener("input", renderMembers);
document.getElementById("members-out").addEventListener("click", async (e) => {
  const t = e.target.closest("button");
  if (!t) return;
  const m = memberships.find((x) => x.id === (t.dataset.home || t.dataset.cancel));
  if (t.dataset.cancel) {
    if (!confirm(`Cancel ${m.name}'s ${m.tier} membership? This stops their monthly payments.`)) return;
    try { await api(`manage/memberships/${encodeURIComponent(m.id)}/cancel`, { method: "POST", body: {} }); await loadMembers(); }
    catch (err) { alert(err.message); }
  } else if (t.dataset.home) {
    openDialog(`Home location for ${m.name}`,
      field("Bronze home location", `<select name="venueSlug">${venues.map((v) => `<option value="${esc(v.slug)}" ${v.slug === m.homeVenueSlug ? "selected" : ""}>${esc(v.city)}</option>`).join("")}</select>`),
      async (form) => {
        await api(`manage/memberships/${encodeURIComponent(m.id)}/home-venue`, { method: "PUT", body: { venueSlug: form.elements.venueSlug.value } });
        await loadMembers();
      });
  }
});

// ---------- enquiries ----------
async function loadEnquiries() {
  const out = document.getElementById("enquiries-out");
  out.innerHTML = `<p class="muted">Loading…</p>`;
  const list = await api("manage/enquiries?days=90");
  out.innerHTML = list.length ? `<div class="table-wrap"><table class="data"><thead><tr><th>Received</th><th>Type</th><th>From</th><th>Message</th></tr></thead><tbody>
    ${list.map((q) => `<tr><td class="small">${esc(fmtDate(q.createdAt))}</td><td>${esc(q.type)}${q.topic ? `<br><span class="small muted">${esc(q.topic)}</span>` : ""}
      ${q.eventDate ? `<br><span class="small">Event: ${esc(fmtDate(q.eventDate))}</span>` : ""}</td>
      <td><b>${esc(q.name)}</b><br><a href="mailto:${esc(q.email)}">${esc(q.email)}</a><br>${esc(q.phone)}</td>
      <td style="white-space:pre-wrap;max-width:420px">${esc(q.message)}</td></tr>`).join("")}
    </tbody></table></div>` : `<p class="muted">No enquiries in the last 90 days.</p>`;
}

// ---------- start ----------
(async () => {
  try {
    const me = await api("me");
    if (me.role !== "Admin") { denied.hidden = false; return; }
    root.hidden = false;
    const start = location.hash.slice(1);
    selectTab(tabs.some((t) => t.dataset.tab === start) ? start : "places");
  } catch (err) {
    if (err instanceof ApiError && err.status === 401) location.assign("/account/?next=/admin/");
    else { denied.hidden = false; denied.textContent = err.message; }
  }
})();
