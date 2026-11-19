const sessionKey = "barikaBountySession";
const portalPaths = { Admin: "/admin.html", OrganizationOwner: "/organization.html", Innovator: "/user.html", Evaluator: "/user.html" };
const form = document.getElementById("registerForm");
const message = document.getElementById("registerMessage");
const organizationFields = document.getElementById("organizationFields");

function selectedRole() {
    return form.elements.role.value;
}

function updateAccountType() {
    const organization = selectedRole() === "OrganizationOwner";
    organizationFields.hidden = !organization;
    organizationFields.querySelectorAll("input").forEach((input) => {
        input.required = organization;
    });
}

function setProviderAvailability(providers) {
    const buttons = [...document.querySelectorAll("[data-provider]")];
    let configured = false;
    buttons.forEach((button) => {
        const key = button.dataset.provider === "linkedin" ? "linkedIn" : button.dataset.provider;
        button.disabled = !providers[key];
        configured = configured || !button.disabled;
    });
    document.querySelectorAll("[data-provider-note]").forEach((note) => {
        note.hidden = configured;
    });
}

async function loadProviders() {
    try {
        const response = await fetch("/api/auth/providers");
        if (!response.ok) throw new Error("Provider status is unavailable.");
        setProviderAvailability(await response.json());
    } catch {
        setProviderAvailability({});
    }
}

function beginSocialRegistration(provider) {
    const fullName = form.elements.fullName.value.trim();
    if (!fullName) {
        message.textContent = "Enter your name before continuing.";
        form.elements.fullName.focus();
        return;
    }

    const query = new URLSearchParams({ mode: "register", role: selectedRole(), fullName });
    if (selectedRole() === "OrganizationOwner") {
        for (const field of ["organizationName", "sector", "location"]) {
            const value = form.elements[field].value.trim();
            if (!value) {
                message.textContent = "Complete your organization details before continuing.";
                form.elements[field].focus();
                return;
            }
            query.set(field, value);
        }
    }
    window.location.assign(`/api/auth/external/${provider}?${query.toString()}`);
}

async function registerWithEmail(event) {
    event.preventDefault();
    message.textContent = "";
    const values = new FormData(form);
    const request = {
        fullName: values.get("fullName"),
        email: values.get("email"),
        password: values.get("password"),
        role: selectedRole(),
        organizationName: values.get("organizationName"),
        sector: values.get("sector"),
        location: values.get("location")
    };

    try {
        const response = await fetch("/api/auth/register", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(request)
        });
        const body = await response.text();
        let result;
        try {
            result = body ? JSON.parse(body) : {};
        } catch {
            result = body;
        }
        if (response.status === 202) {
            window.location.assign("/auth-complete.html?pending=1");
            return;
        }
        if (!response.ok) throw new Error(typeof result === "string" ? result : result.message || "Registration could not be completed.");
        sessionStorage.setItem(sessionKey, JSON.stringify({ token: result.token, user: result.user }));
        window.location.assign(portalPaths[result.user.role] || "/");
    } catch (error) {
        message.textContent = error instanceof TypeError
            ? "Could not reach the registration service. Please try again."
            : typeof error.message === "string" ? error.message : "Registration could not be completed.";
    }
}

document.querySelectorAll('input[name="role"]').forEach((input) => input.addEventListener("change", updateAccountType));
form.addEventListener("submit", registerWithEmail);
document.querySelectorAll("[data-provider]").forEach((button) => {
    button.addEventListener("click", () => beginSocialRegistration(button.dataset.provider));
});
updateAccountType();
loadProviders();

if (new URLSearchParams(window.location.search).get("error") === "provider") {
    message.textContent = "Social sign-in could not be completed. Please try again or use email and password.";
}
