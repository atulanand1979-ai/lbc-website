// Pay As You Go: choose place -> weekly class -> date -> details -> booked (pay at the class).
import { api, esc, getConfig, handleSubmit, money, mountCaptcha, showMessage } from "./api.js";

const form = document.getElementById("book-form");
const venueBox = document.getElementById("venue-options");
const classStep = document.getElementById("step-class");
const classBox = document.getElementById("class-options");
const dateStep = document.getElementById("step-date");
const dateBox = document.getElementById("date-options");
const detailsStep = document.getElementById("step-details");
const summary = document.getElementById("book-summary");
const message = form.querySelector("[data-message]");

let venues = [];
const time = (hhmm) => {
  const [h, m] = hhmm.split(":").map(Number);
  const hour = h % 12 === 0 ? 12 : h % 12;
  return `${hour}${m ? `:${String(m).padStart(2, "0")}` : ""}${h < 12 ? "am" : "pm"}`;
};
const params = new URLSearchParams(location.search);

const option = (name, value, title, sub, checked = false) => `
  <label class="option"><input type="radio" name="${name}" value="${esc(value)}" ${checked ? "checked" : ""} required>
  <span>${esc(title)}${sub ? `<small>${esc(sub)}</small>` : ""}</span></label>`;

function show(el, visible) { el.hidden = !visible; }

function selected(name) { return form.querySelector(`input[name="${name}"]:checked`)?.value; }

async function onVenueChange() {
  const venue = venues.find((v) => v.slug === selected("venueSlug"));
  show(dateStep, false); show(detailsStep, false);
  if (!venue) return;
  classBox.innerHTML = venue.classes.length
    ? venue.classes.map((c, i) => option("classId", c.id, c.level, `Every ${c.dayOfWeek} ${time(c.startTime)}–${time(c.endTime)}`, venue.classes.length === 1 && i === 0)).join("")
    : `<p class="muted">No classes are scheduled here yet.</p>`;
  show(classStep, true);
  if (venue.classes.length === 1) await onClassChange();
}

async function onClassChange() {
  const venueSlug = selected("venueSlug");
  const classId = selected("classId");
  show(detailsStep, false);
  if (!classId) return;
  dateBox.innerHTML = `<p class="muted">Loading dates…</p>`;
  show(dateStep, true);
  try {
    const dates = await api(`venues/${encodeURIComponent(venueSlug)}/classes/${encodeURIComponent(classId)}/dates`);
    dateBox.innerHTML = dates.length
      ? dates.map((d) => option("date", d.date, d.label)).join("")
      : `<p class="muted">There are no upcoming dates for this class right now.</p>`;
  } catch (err) {
    dateBox.innerHTML = `<p class="alert alert--error">${esc(err.message)}</p>`;
  }
}

async function onDateChange() {
  const venue = venues.find((v) => v.slug === selected("venueSlug"));
  const cls = venue?.classes.find((c) => c.id === selected("classId"));
  const date = dateBox.querySelector("input:checked")?.closest("label").textContent.trim();
  const { prices } = await getConfig();
  summary.innerHTML = `<span>${esc(venue.city)} · ${esc(cls.label)}<br><span class="muted">${esc(date)}</span></span>
    <span class="summary__price">${prices ? `${money(prices.payAsYouGoPence)}<br><small class="muted">pay at class</small>` : ""}</span>`;
  show(detailsStep, true);
}

form.addEventListener("change", (e) => {
  if (e.target.name === "venueSlug") onVenueChange();
  else if (e.target.name === "classId") onClassChange();
  else if (e.target.name === "date") onDateChange();
});

const captcha = mountCaptcha(form);
handleSubmit(form, async (data) => {
  const captchaToken = (await captcha)();
  const { redirectUrl } = await api("bookings", { method: "POST", body: { ...data, captchaToken } });
  location.assign(redirectUrl);
});

(async () => {
  try {
    venues = (await api("venues")).filter((v) => v.classes.length);
    const wanted = params.get("venue");
    venueBox.innerHTML = venues.map((v) => option("venueSlug", v.slug, v.city, v.name, v.slug === wanted)).join("");
    if (wanted && venues.some((v) => v.slug === wanted)) await onVenueChange();
  } catch (err) {
    venueBox.innerHTML = `<p class="alert alert--error">${esc(err.message)}</p>`;
  }
})();
