const sessionKey = "barikaBountySession";
const portalPaths = {
    Admin: "/admin.html",
    OrganizationOwner: "/organization.html",
    Innovator: "/user.html",
    Evaluator: "/user.html"
};
const byId = (id) => document.getElementById(id);

function escapeHtml(value) {
    const element = document.createElement("div");
    element.textContent = value ?? "";
    return element.innerHTML;
}

function showLogin() {
    byId("loginError").textContent = "";
    byId("loginDialog").showModal();
}

async function loadChallenges() {
    const params = new URLSearchParams();
    const search = byId("problemSearch").value.trim();
    const categoryId = byId("categoryFilter").value;
    if (search) params.set("search", search);
    if (categoryId) params.set("categoryId", categoryId);

    const response = await fetch(`/api/problems?${params.toString()}`);
    if (!response.ok) throw new Error("Could not load open challenges. Please refresh the page.");
    const problems = await response.json();
    byId("problemEmpty").hidden = problems.length !== 0;
    byId("problems").innerHTML = problems.map((problem) => {
        const category = window.challengeCategories.find((item) => item.id === problem.categoryId);
        const organization = window.challengeOrganizations.find((item) => item.id === problem.organizationId);
        const amount = problem.bountyAmount ?? window.challengeRewards.find((reward) => reward.problemId === problem.id)?.amount;
        const rewards = [];
        if (Number(amount) > 0) rewards.push(`Bounty: KES ${Number(amount).toLocaleString("en-KE")}`);
        if (problem.offersEmployment) rewards.push("Employment opportunity");
        const bounty = rewards.length ? rewards.join(" · ") : "Bounty not set";
        return `<article class="challenge-card"><span class="tag">Open</span><h3>${escapeHtml(problem.title)}</h3><p>${escapeHtml(problem.description)}</p><div class="card-meta"><span>${escapeHtml(organization?.name || "Community partner")}</span><span>${escapeHtml(category?.name || "General")}</span><span>Closes ${new Date(problem.deadline).toLocaleDateString()}</span><strong>${escapeHtml(bounty)}</strong></div></article>`;
    }).join("");
}

function portalFor(user) {
    return portalPaths[user.role] || "/";
}

async function configureSocialSignIn() {
    const buttons = [...document.querySelectorAll("[data-provider]")];
    if (buttons.length === 0) return;
    try {
        const response = await fetch("/api/auth/providers");
        if (!response.ok) return;
        const providers = await response.json();
        let configured = false;
        buttons.forEach((button) => {
            button.disabled = !providers[button.dataset.provider === "linkedin" ? "linkedIn" : button.dataset.provider];
            configured ||= !button.disabled;
        });
        document.querySelectorAll("[data-provider-note]").forEach((note) => {
            note.hidden = configured;
        });
    } catch {
        document.querySelectorAll("[data-provider-note]").forEach((note) => {
            note.textContent = "Social sign-in is unavailable. Use email and password instead.";
        });
    }
}

function startExternalSignIn(provider) {
    const query = new URLSearchParams({ mode: "signin" });
    window.location.assign(`/api/auth/external/${provider}?${query.toString()}`);
}

async function initialize() {
    const savedSession = sessionStorage.getItem(sessionKey);
    if (savedSession) {
        try {
            const session = JSON.parse(savedSession);
            if (session.user && portalPaths[session.user.role]) {
                window.location.replace(portalFor(session.user));
                return;
            }
        } catch {
            sessionStorage.removeItem(sessionKey);
        }
    }

    const lookupResponse = await fetch("/api/lookups");
    if (!lookupResponse.ok) throw new Error("Could not load challenge categories. Please refresh the page.");
    const lookups = await lookupResponse.json();
    window.challengeCategories = lookups.categories;
    window.challengeOrganizations = lookups.organizations;
    window.challengeRewards = lookups.rewards;
    const categoryFilter = byId("categoryFilter");
    const allCategories = document.createElement("option");
    allCategories.value = "";
    allCategories.textContent = "All topics";
    categoryFilter.appendChild(allCategories);
    lookups.categories.forEach((category) => {
        const option = document.createElement("option");
        option.value = category.id;
        option.textContent = category.name;
        categoryFilter.appendChild(option);
    });

    await loadChallenges();
    await configureSocialSignIn();
    ["problemSearch", "categoryFilter"].forEach((id) => {
        byId(id).addEventListener("input", () => loadChallenges().catch((error) => { byId("pageError").textContent = error.message; }));
    });

    byId("openLogin").addEventListener("click", showLogin);
    byId("heroLogin").addEventListener("click", showLogin);
    byId("closeLogin").addEventListener("click", () => byId("loginDialog").close());
    document.querySelectorAll("#loginDialog [data-provider]").forEach((button) => {
        button.addEventListener("click", () => startExternalSignIn(button.dataset.provider));
    });
    byId("loginForm").addEventListener("submit", async (event) => {
        event.preventDefault();
        const errorElement = byId("loginError");
        errorElement.textContent = "";
        try {
            const response = await fetch("/api/auth/login", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    email: byId("email").value.trim(),
                    password: byId("password").value
                })
            });
            if (response.status === 401) {
                errorElement.textContent = "Sign-in failed. Check your details; organization accounts may need administrator approval.";
                return;
            }
            if (!response.ok) throw new Error("The sign-in service could not complete your request.");

            const result = await response.json();
            sessionStorage.setItem(sessionKey, JSON.stringify({ token: result.token, user: result.user }));
            window.location.assign(portalFor(result.user));
        } catch (error) {
            errorElement.textContent = error instanceof TypeError
                ? "Could not reach the sign-in service. Please try again."
                : error.message;
        }
    });
    if (new URLSearchParams(window.location.search).get("login") === "1") showLogin();
}

window.challengeCategories = [];
window.challengeOrganizations = [];
window.challengeRewards = [];
initialize().catch((error) => { byId("pageError").textContent = error.message; });
