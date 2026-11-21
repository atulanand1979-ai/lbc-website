// Replaces the build-time venue list with live data from the API (admin edits show immediately).
import { api, esc } from "./api.js";

const list = document.querySelector("[data-venues]");
const DAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
const time = (hhmm) => {
  const [h, m] = hhmm.split(":").map(Number);
  const suffix = h < 12 ? "am" : "pm";
  const hour = h % 12 === 0 ? 12 : h % 12;
  return m ? `${hour}:${String(m).padStart(2, "0")}${suffix}` : `${hour}${suffix}`;
};

function render(v) {
  const days = [...new Set(v.classes.map((c) => c.dayOfWeek))].sort((a, b) => DAYS.indexOf(a) - DAYS.indexOf(b));
  const soon = v.startsOn && new Date(v.startsOn) > new Date()
    ? `<span class="venue-card__soon">Starting ${esc(new Date(v.startsOn).toLocaleDateString("en-GB", { day: "numeric", month: "long", year: "numeric" }))}</span>` : "";
  const classes = v.classes.map((c) => `<li>${esc(c.level)}: ${time(c.startTime)} – ${time(c.endTime)}</li>`).join("");
  const map = v.mapUrl || `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(`${v.name}, ${v.address}, ${v.postcode}`)}`;
  return `<article class="venue-card" id="${esc(v.slug)}">
    <span class="venue-card__city">${esc(v.city)}</span>
    ${soon}
    ${days.map((d) => `<span class="venue-card__day">${esc(d)}</span>`).join("")}
    <ul class="venue-card__classes">${classes || "<li>Class times coming soon</li>"}</ul>
    <p class="venue-card__address">${esc(v.name)}<br>${esc(v.address)}, ${esc(v.postcode)}</p>
    <div class="btn-row">
      <a class="btn btn--yellow-outline btn--small" href="/book/?venue=${encodeURIComponent(v.slug)}">Book a class</a>
      <a class="btn btn--small" href="${esc(map)}" target="_blank" rel="noopener">Map</a>
    </div>
  </article>`;
}

if (list) {
  api("venues")
    .then((venues) => { if (venues.length) list.innerHTML = venues.map(render).join(""); })
    .catch(() => { /* keep the build-time list */ });
}
