// Set-password (from emailed link) and forgot-password forms.
import { api, handleSubmit, showMessage } from "./api.js";

const setForm = document.getElementById("set-password-form");
if (setForm) {
  const token = new URLSearchParams(location.search).get("token") || "";
  handleSubmit(setForm, async (data) => {
    if (data.password !== data.confirm) throw new Error("The two passwords don't match.");
    const r = await api("auth/set-password", { method: "POST", body: { token, password: data.password } });
    location.assign(r.role === "Admin" ? "/admin/" : "/account/");
  });
}

const forgotForm = document.getElementById("forgot-form");
if (forgotForm) {
  handleSubmit(forgotForm, async (data) => {
    await api("auth/forgot", { method: "POST", body: data });
    forgotForm.reset();
    showMessage(forgotForm.querySelector("[data-message]"), "success",
      "If there's an account for that email, we've sent a link. Please check your inbox (and spam folder).");
  });
}
