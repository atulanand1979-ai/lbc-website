// Enquiry forms (contact, workshops, book us, one-to-one, private group). Any form with [data-enquiry].
import { api, handleSubmit, mountCaptcha, showMessage } from "./api.js";

for (const form of document.querySelectorAll("form[data-enquiry]")) {
  const captcha = mountCaptcha(form);
  handleSubmit(form, async (data) => {
    const captchaToken = (await captcha)();
    await api("enquiries", { method: "POST", body: { ...data, type: form.dataset.enquiry, captchaToken } });
    form.reset();
    showMessage(form.querySelector("[data-message]"), "success", "Thanks! Your message has been sent and the team will get back to you soon.");
  });
}
