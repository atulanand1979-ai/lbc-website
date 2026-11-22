// Membership sign-up: choose tier (+ home location for Bronze) -> details -> pay monthly.
import { api, esc, getConfig, handleSubmit, money, mountCaptcha, showMessage } from "./api.js";

const form = document.getElementById("join-form");
const venueField = document.getElementById("home-venue-field");
const venueSelect = document.getElementById("homeVenueSlug");
const summary = document.getElementById("join-summary");
const message = form.querySelector("[data-message]");
const params = new URLSearchParams(location.search);

const TIERS = {
  bronze: { name: "Bronze", text: "Unlimited Beginners classes within one city." },
  silver: { name: "Silver", text: "Unlimited Beginners classes across all locations." },
  gold: { name: "Gold", text: "Unlimited Beginners and advanced classes across all locations, plus Gold perks." },
};

async function update() {
  const tier = form.querySelector("input[name=tier]:checked")?.value;
  const isBronze = tier === "bronze";
  venueField.hidden = !isBronze;
  venueSelect.required = isBronze;
  if (!tier) { summary.hidden = true; return; }
  const { prices } = await getConfig();
  const pence = prices?.[`${tier}Pence`];
  summary.innerHTML = `<span>${esc(TIERS[tier].name)} membership<br><span class="muted">${esc(TIERS[tier].text)}</span></span>
    <span class="summary__price">${pence ? `${money(pence)}<span class="price-card__per"> /month</span>` : ""}</span>`;
  summary.hidden = false;
}

form.addEventListener("change", (e) => { if (e.target.name === "tier") update(); });

const wanted = params.get("tier");
if (wanted && TIERS[wanted]) form.querySelector(`input[name=tier][value=${wanted}]`).checked = true;
if (params.get("cancelled")) showMessage(message, "info", "Direct Debit set-up was cancelled — no membership has been set up.");

(async () => {
  try {
    const venues = await api("venues");
    venueSelect.innerHTML = `<option value="">Choose your city…</option>` +
      venues.map((v) => `<option value="${esc(v.slug)}">${esc(v.city)} — ${esc(v.name)}</option>`).join("");
  } catch { /* the API error will surface on submit */ }
  update();
})();

const captcha = mountCaptcha(form);
handleSubmit(form, async (data) => {
  if (data.tier !== "bronze") delete data.homeVenueSlug;
  const captchaToken = (await captcha)();
  const { redirectUrl } = await api("memberships", { method: "POST", body: { ...data, captchaToken } });
  location.assign(redirectUrl);
});
