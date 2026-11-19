const sessionKey = "barikaBountySession";
const portalPaths = {
    Admin: "/admin.html",
    OrganizationOwner: "/organization.html",
    Innovator: "/user.html",
};
const session = readSession();
const expectedRole = document.body.dataset.portal;
const byId = (id) => document.getElementById(id);
let lookups = { users: [], organizations: [], categories: [], skills: [], rewards: [] };
let challenges = [];

function readSession() {
    try {
        return JSON.parse(sessionStorage.getItem(sessionKey) || "null");
    } catch {
        return null;
    }
}

function escapeHtml(value) {
    const element = document.createElement("div");
    element.textContent = value ?? "";
    return element.innerHTML;
}

function showMessage(message) {
    byId("portalMessage").textContent = message;
}

function formatBounty(problem) {
    const amount = problem.bountyAmount ?? lookups.rewards?.find((reward) => reward.problemId === problem.id)?.amount;
    const rewards = [];
    if (Number(amount) > 0) rewards.push(`Bounty: KES ${Number(amount).toLocaleString("en-KE")}`);
    if (problem.offersEmployment) rewards.push("Employment opportunity");
    return rewards.length ? rewards.join(" · ") : "Bounty not set";
}

async function api(url, options = {}) {
    const response = await fetch(url, {
        ...options,
        headers: {
            ...(options.body ? { "Content-Type": "application/json" } : {}),
            Authorization: `Bearer ${session.token}`,
            ...(options.headers || {})
        }
    });
    if (response.status === 401) {
        sessionStorage.removeItem(sessionKey);
        window.location.replace("/");
        throw new Error("Your session has expired. Please sign in again.");
    }
    if (!response.ok) {
        const detail = await response.text();
        throw new Error(detail || `Request failed (${response.status}).`);
    }
    return response.status === 204 ? null : response.json();
}

function fillSelect(select, items, label, includeAllLabel) {
    if (!select) return;
    select.innerHTML = includeAllLabel ? `<option value="">${includeAllLabel}</option>` : "";
    items.forEach((item) => {
        const option = document.createElement("option");
        option.value = item.id;
        option.textContent = label(item);
        select.appendChild(option);
    });
}

function renderDirectory() {
    const userRows = byId("directoryUsers");
    if (userRows) {
        userRows.innerHTML = lookups.users.map((user) => {
            const organization = lookups.organizations.find((item) => item.id === user.organizationId);
            const statusLabel = !user.isActive && user.role === "OrganizationOwner" ? "Pending approval" : user.isActive ? "Active" : "Inactive";
            const adminCells = expectedRole === "Admin"
                ? `<td>${escapeHtml(organization?.name || "—")}</td><td><span class="tag ${user.isActive ? "" : "tag-inactive"}">${statusLabel}</span></td><td><button class="button button-small" type="button" data-toggle-user="${user.id}" data-active="${user.isActive}" ${user.id === session.user.id ? "disabled" : ""}>${user.isActive ? "Deactivate" : "Approve / reactivate"}</button></td>`
                : "";
            return `<tr><td>${escapeHtml(user.fullName)}</td><td>${escapeHtml(user.email)}</td><td>${escapeHtml(user.role)}</td>${adminCells}</tr>`;
        }).join("");
    }
    const organizationRows = byId("directoryOrganizations");
    if (organizationRows) {
        organizationRows.innerHTML = lookups.organizations.map((organization) => expectedRole === "Admin"
            ? `<tr data-organization-id="${organization.id}"><td><input data-organization-field="name" value="${escapeHtml(organization.name)}" aria-label="Organization name"></td><td><input data-organization-field="sector" value="${escapeHtml(organization.sector)}" aria-label="Organization sector"></td><td><input data-organization-field="location" value="${escapeHtml(organization.location)}" aria-label="Organization location"></td><td><button class="button button-small" type="button" data-save-organization="${organization.id}">Save</button> <button class="button button-small button-danger" type="button" data-delete-organization="${organization.id}">Remove</button></td></tr>`
            : `<tr><td>${escapeHtml(organization.name)}</td><td>${escapeHtml(organization.sector)}</td><td>${escapeHtml(organization.location)}</td></tr>`).join("");
    }
    fillSelect(byId("problemOrganization"), lookups.organizations, (item) => item.name);
    fillSelect(byId("userOrganization"), lookups.organizations, (item) => item.name, "No organization");
    fillSelect(byId("problemCategory"), lookups.categories, (item) => item.name);
    const skills = byId("skillChecks");
    if (skills) {
        skills.innerHTML = lookups.skills.map((skill) =>
            `<label><input type="checkbox" value="${skill.id}"><span>${escapeHtml(skill.name)}</span></label>`).join("");
    }
    if (expectedRole === "OrganizationOwner" && byId("problemOrganization")) {
        byId("problemOrganization").value = String(session.user.organizationId);
        byId("problemOrganization").disabled = true;
    }
    if (byId("portalHeading")) {
        byId("portalHeading").textContent = lookups.organizations[0]?.name || "Your organization";
    }
}

async function loadDashboard() {
    if (!byId("metrics")) return;
    const dashboard = await api("/api/dashboard");
    const totals = dashboard.totals;
    const metrics = [["Challenges", totals.problems], ["Proposals", totals.proposals], ["Solutions", totals.solutions], ["Average review", `${dashboard.averageEvaluationScore}%`]];
    byId("metrics").innerHTML = metrics.map(([label, value]) =>
        `<article class="metric"><strong>${value}</strong><span>${label}</span></article>`).join("");
}

async function loadProblems() {
    if (!byId("problems")) return;
    const params = new URLSearchParams();
    const search = byId("problemSearch")?.value.trim();
    const categoryId = byId("categoryFilter")?.value;
    const status = byId("statusFilter")?.value;
    if (search) params.set("search", search);
    if (categoryId) params.set("categoryId", categoryId);
    if (status) params.set("status", status);
    const problems = await api(`/api/problems?${params.toString()}`);
    challenges = problems;
    fillSelect(
        byId("proposalProblem"),
        problems.filter((item) => item.status === "Open"),
        (item) => {
            const category = lookups.categories.find((category) => category.id === item.categoryId);
            return `${item.title} · ${category?.name || "General"}`;
        },
        "Choose an open challenge"
    );
    updateProposalWorkArea();
    if (byId("problemEmpty")) byId("problemEmpty").hidden = problems.length !== 0;
    byId("problems").innerHTML = problems.map((problem) => {
        const organization = lookups.organizations.find((item) => item.id === problem.organizationId);
        const category = lookups.categories.find((item) => item.id === problem.categoryId);
        const canArchive = expectedRole === "Admin" && problem.status !== "Archived";
        const archiveButton = canArchive
            ? `<button class="button button-danger" type="button" data-archive-problem="${problem.id}">Archive challenge</button>`
            : "";
        const canEdit = ["Admin", "OrganizationOwner"].includes(expectedRole) && problem.status !== "Archived";
        const editForm = canEdit
            ? `<details class="challenge-edit"><summary>Edit challenge</summary><form data-edit-problem="${problem.id}"><input type="hidden" name="requiredSkillIds" value="${escapeHtml(JSON.stringify(problem.requiredSkillIds))}"><label>Title<input name="title" value="${escapeHtml(problem.title)}" required></label><label>Description<textarea name="description" required>${escapeHtml(problem.description)}</textarea></label><div class="field-row"><label>Organization<select name="organizationId" data-edit-organization required></select></label><label>Category<select name="categoryId" data-edit-category required></select></label></div><label>Deadline<input name="deadline" type="date" value="${new Date(problem.deadline).toISOString().slice(0, 10)}" required></label><label>Bounty amount (KES)<input name="bountyAmount" type="number" min="0" step="1" value="${problem.bountyAmount ?? lookups.rewards?.find((reward) => reward.problemId === problem.id)?.amount ?? 0}" required></label><label class="employment-option"><input name="offersEmployment" type="checkbox" ${problem.offersEmployment ? "checked" : ""}> Employment opportunity included</label><button class="button button-secondary" type="submit">Save challenge</button></form></details>`
            : "";
        const status = problem.status.replace(/([A-Z])/g, " $1").trim();
        return `<article class="challenge-card"><span class="tag">${escapeHtml(status)}</span><h3>${escapeHtml(problem.title)}</h3><p>${escapeHtml(problem.description)}</p><div class="card-meta"><span>${escapeHtml(organization?.name || "Community partner")}</span><span>${escapeHtml(category?.name || "General")}</span><span>Deadline ${new Date(problem.deadline).toLocaleDateString()}</span><strong>${escapeHtml(formatBounty(problem))}</strong></div>${editForm}${archiveButton}</article>`;
    }).join("");
    byId("problems").querySelectorAll("[data-edit-problem]").forEach((form) => {
        const problemId = Number(form.dataset.editProblem);
        const problem = problems.find((item) => item.id === problemId);
        const orgs = expectedRole === "OrganizationOwner"
            ? lookups.organizations.filter((item) => item.id === session.user.organizationId)
            : lookups.organizations;
        fillSelect(form.querySelector("[data-edit-organization]"), orgs, (item) => item.name);
        fillSelect(form.querySelector("[data-edit-category]"), lookups.categories, (item) => item.name);
        form.elements.organizationId.value = String(problem.organizationId);
        form.elements.categoryId.value = String(problem.categoryId);
    });
}

function updateProposalWorkArea() {
    const context = byId("proposalCategoryHint");
    if (!context) return;

    const problemId = Number(byId("proposalProblem")?.value);
    const challenge = challenges.find((item) => item.id === problemId);
    const category = challenge && lookups.categories.find((item) => item.id === challenge.categoryId);
    if (!challenge || !category) {
        context.textContent = "Choose a challenge to see its work area.";
        return;
    }

    context.innerHTML = `<span class="proposal-context-label">Work area · ${escapeHtml(category.name)}</span><span>${escapeHtml(category.description)}</span><span>Your proposal will be listed under this work area.</span>`;
}

function updateAdminNavigation() {
    const links = [...document.querySelectorAll(".admin-nav a")];
    if (!links.length) return;

    const currentHash = window.location.hash || "#overview";
    const activeLink = links.find((link) => link.getAttribute("href") === currentHash) || links[0];
    links.forEach((link) => {
        if (link === activeLink) link.setAttribute("aria-current", "page");
        else link.removeAttribute("aria-current");
    });
}

async function loadProposals() {
    if (!byId("proposalsList")) return;
    const params = new URLSearchParams();
    const search = byId("proposalSearch")?.value.trim();
    const status = byId("proposalStatus")?.value;
    if (search) params.set("search", search);
    if (status) params.set("status", status);
    const rows = await api(`/api/proposals?${params.toString()}`);
    const canReview = ["Admin", "OrganizationOwner"].includes(expectedRole);
    byId("proposalsList").innerHTML = rows.map((row) => {
        const statusControl = canReview
            ? `<label class="proposal-stage"><span class="proposal-label">Update stage</span><select class="proposal-status-control" data-proposal-id="${row.proposal.id}" aria-label="Update status for ${escapeHtml(row.proposal.title)}">${["Submitted", "Shortlisted", "Rejected", "Approved", "Implemented"].map((item) => `<option value="${item}"${row.proposal.status === item ? " selected" : ""}>${item}</option>`).join("")}</select></label>`
            : `<span class="tag">${escapeHtml(row.proposal.status)}</span>`;
        const category = row.category?.name || "Uncategorized";
        return `<article class="proposal-row"><div><span class="proposal-label">Proposal</span><h3>${escapeHtml(row.proposal.title)}</h3></div><div><span class="proposal-label">Work area</span><span class="tag proposal-work-area">${escapeHtml(category)}</span></div><div><span class="proposal-label">Challenge</span><p>${escapeHtml(row.problem?.title || "Challenge unavailable")}</p></div><div><span class="proposal-label">Submitted by</span><p>${escapeHtml(row.user?.fullName || "Unknown innovator")}</p></div><div><span class="proposal-label">Stage</span>${statusControl}</div></article>`;
    }).join("") || '<p class="empty-state">No proposals match those filters yet.</p>';
}

async function loadCategorySuggestions() {
    const list = byId("categorySuggestionsList");
    if (!list) return;

    const suggestions = await api("/api/category-suggestions");
    list.innerHTML = suggestions.map((suggestion) => {
        const status = suggestion.status.toLowerCase();
        const reviewActions = expectedRole === "Admin" && status === "pending"
            ? `<div class="category-suggestion-actions"><button class="button button-primary" type="button" data-review-category-suggestion="${suggestion.id}" data-status="Approved">Approve</button><button class="button button-danger" type="button" data-review-category-suggestion="${suggestion.id}" data-status="Rejected">Reject</button></div>`
            : "";
        return `<article class="category-suggestion-row"><div><span class="tag ${status === "pending" ? "tag-pending" : ""}">${escapeHtml(suggestion.status)}</span><h3>${escapeHtml(suggestion.name)}</h3><p>${escapeHtml(suggestion.description)}</p><small>Suggested by ${escapeHtml(suggestion.submittedBy)} · ${new Date(suggestion.submittedAt).toLocaleDateString()}</small></div>${reviewActions}</article>`;
    }).join("") || `<p class="empty-state">${expectedRole === "Admin" ? "No category suggestions are awaiting review." : "You have not suggested any categories yet."}</p>`;
}

async function refresh() {
    await Promise.all([loadProblems(), loadProposals(), loadDashboard(), loadCategorySuggestions()]);
}

async function initialize() {
    if (!session?.token || !session.user) {
        window.location.replace("/");
        return;
    }
    const roleMatchesPortal = expectedRole === "Member"
        ? ["Innovator", "Evaluator"].includes(session.user.role)
        : session.user.role === expectedRole;
    if (!roleMatchesPortal) {
        window.location.replace(portalPaths[session.user.role] || "/");
        return;
    }

    byId("portalUser").textContent = session.user.fullName;
    if (expectedRole === "Admin") {
        updateAdminNavigation();
        window.addEventListener("hashchange", updateAdminNavigation);
    }
    if (expectedRole === "Member" && session.user.role === "Evaluator") {
        byId("userKicker").textContent = "User workspace";
        byId("userHeading").textContent = "Proposal review queue";
        byId("userDescription").textContent = "Review submitted ideas and their challenge context.";
        byId("userWorkLink").hidden = true;
        byId("userProposalsLink").textContent = "Proposal queue";
        byId("workspace").hidden = true;
        byId("userProposalHeading").textContent = "Submitted proposals";
        byId("userProposalDescription").textContent = "Proposal stages are read-only in the user portal.";
    }
    const lookupResponse = await api("/api/lookups");
    lookups = lookupResponse;
    renderDirectory();
    fillSelect(byId("categoryFilter"), lookups.categories, (item) => item.name, "All topics");
    await refresh();

    ["problemSearch", "categoryFilter", "statusFilter", "proposalSearch", "proposalStatus"].forEach((id) => {
        const element = byId(id);
        if (element) element.addEventListener("input", () => refresh().catch((error) => showMessage(error.message)));
    });

    byId("signOut").addEventListener("click", async () => {
        try {
            await api("/api/auth/logout", { method: "POST", keepalive: true });
        } catch {
            showMessage("The session could not be revoked because the app is unreachable.");
        } finally {
            sessionStorage.removeItem(sessionKey);
            window.location.assign("/");
        }
    });

    byId("problems")?.addEventListener("click", async (event) => {
        const button = event.target.closest("[data-archive-problem]");
        if (!button || !window.confirm("Archive this challenge? It will leave active portals, and its proposal history will be kept.")) return;
        button.disabled = true;
        try {
            await api(`/api/problems/${button.dataset.archiveProblem}/archive`, { method: "POST" });
            await refresh();
            showMessage("Challenge archived. Its proposal history has been retained.");
        } catch (error) {
            button.disabled = false;
            showMessage(error.message);
        }
    });

    byId("problemForm")?.addEventListener("submit", async (event) => {
        event.preventDefault();
        const formElement = event.currentTarget;
        const submitButton = formElement.querySelector("[type=submit]");
        submitButton.disabled = true;
        const form = new FormData(formElement);
        const request = {
            title: form.get("title"),
            description: form.get("description"),
            organizationId: expectedRole === "OrganizationOwner" ? session.user.organizationId : Number(form.get("organizationId")),
            categoryId: Number(form.get("categoryId")),
            deadline: form.get("deadline"),
            requiredSkillIds: [...byId("skillChecks").querySelectorAll("input:checked")].map((item) => Number(item.value)),
            bountyAmount: Number(form.get("bountyAmount")),
            offersEmployment: form.get("offersEmployment") === "on"
        };
        try {
            await api("/api/problems", { method: "POST", body: JSON.stringify(request) });
            formElement.reset();
            renderDirectory();
            await refresh();
            showMessage("Challenge published.");
        } catch (error) {
            showMessage(error.message);
        } finally {
            submitButton.disabled = false;
        }
    });

    byId("proposalForm")?.addEventListener("submit", async (event) => {
        event.preventDefault();
        const formElement = event.currentTarget;
        const submitButton = formElement.querySelector("[type=submit]");
        submitButton.disabled = true;
        const form = new FormData(formElement);
        try {
            await api("/api/proposals", {
                method: "POST",
                body: JSON.stringify({
                    problemId: Number(form.get("problemId")),
                    title: form.get("title"),
                    summary: form.get("summary")
                })
            });
            formElement.reset();
            await refresh();
            showMessage("Proposal submitted.");
        } catch (error) {
            showMessage(error.message);
        } finally {
            submitButton.disabled = false;
        }
    });
    byId("proposalProblem")?.addEventListener("change", updateProposalWorkArea);

    byId("categorySuggestionForm")?.addEventListener("submit", async (event) => {
        event.preventDefault();
        const formElement = event.currentTarget;
        const form = new FormData(formElement);
        const submitButton = formElement.querySelector("[type=submit]");
        submitButton.disabled = true;
        try {
            await api("/api/category-suggestions", {
                method: "POST",
                body: JSON.stringify({
                    name: form.get("name"),
                    description: form.get("description")
                })
            });
            formElement.reset();
            await loadCategorySuggestions();
            showMessage("Category suggestion submitted for admin review.");
        } catch (error) {
            showMessage(error.message);
        } finally {
            submitButton.disabled = false;
        }
    });

    byId("userForm")?.addEventListener("submit", async (event) => {
        event.preventDefault();
        const formElement = event.currentTarget;
        const form = new FormData(formElement);
        const organizationId = form.get("organizationId");
        const submitButton = formElement.querySelector("[type=submit]");
        submitButton.disabled = true;
        try {
            await api("/api/users", {
                method: "POST",
                body: JSON.stringify({
                    fullName: form.get("fullName"),
                    email: form.get("email"),
                    password: form.get("password"),
                    role: form.get("role"),
                    organizationId: organizationId ? Number(organizationId) : null
                })
            });
            formElement.reset();
            await reloadDirectory();
            showMessage("Member account created.");
        } catch (error) {
            showMessage(error.message);
        } finally {
            submitButton.disabled = false;
        }
    });

    byId("directoryUsers")?.addEventListener("click", async (event) => {
        const button = event.target.closest("[data-toggle-user]");
        if (!button) return;
        button.disabled = true;
        try {
            await api(`/api/users/${button.dataset.toggleUser}/status`, {
                method: "PATCH",
                body: JSON.stringify({ isActive: button.dataset.active !== "true" })
            });
            await reloadDirectory();
            showMessage("Member account status updated.");
        } catch (error) {
            button.disabled = false;
            showMessage(error.message);
        }
    });

    byId("organizationForm")?.addEventListener("submit", async (event) => {
        event.preventDefault();
        const formElement = event.currentTarget;
        const form = new FormData(formElement);
        const submitButton = formElement.querySelector("[type=submit]");
        submitButton.disabled = true;
        try {
            await api("/api/organizations", {
                method: "POST",
                body: JSON.stringify({
                    name: form.get("name"),
                    sector: form.get("sector"),
                    location: form.get("location")
                })
            });
            formElement.reset();
            await reloadDirectory();
            showMessage("Organization created.");
        } catch (error) {
            showMessage(error.message);
        } finally {
            submitButton.disabled = false;
        }
    });

    byId("directoryOrganizations")?.addEventListener("click", async (event) => {
        const saveButton = event.target.closest("[data-save-organization]");
        const deleteButton = event.target.closest("[data-delete-organization]");
        const button = saveButton || deleteButton;
        if (!button) return;
        const organizationId = button.dataset.saveOrganization || button.dataset.deleteOrganization;
        button.disabled = true;
        try {
            if (saveButton) {
                const row = saveButton.closest("tr");
                await api(`/api/organizations/${organizationId}`, {
                    method: "PUT",
                    body: JSON.stringify({
                        name: row.querySelector('[data-organization-field="name"]').value,
                        sector: row.querySelector('[data-organization-field="sector"]').value,
                        location: row.querySelector('[data-organization-field="location"]').value
                    })
                });
                showMessage("Organization updated.");
            } else {
                if (!window.confirm("Remove this organization? Organizations with members or challenges cannot be removed.")) {
                    button.disabled = false;
                    return;
                }
                await api(`/api/organizations/${organizationId}`, { method: "DELETE" });
                showMessage("Organization removed.");
            }
            await reloadDirectory();
            await refresh();
        } catch (error) {
            button.disabled = false;
            showMessage(error.message);
        }
    });

    byId("categorySuggestionsList")?.addEventListener("click", async (event) => {
        const button = event.target.closest("[data-review-category-suggestion]");
        if (!button) return;
        button.disabled = true;
        try {
            await api(`/api/category-suggestions/${button.dataset.reviewCategorySuggestion}/status`, {
                method: "PATCH",
                body: JSON.stringify({ status: button.dataset.status })
            });
            if (button.dataset.status === "Approved") await reloadDirectory();
            await refresh();
            showMessage(`Category suggestion ${button.dataset.status.toLowerCase()}.`);
        } catch (error) {
            button.disabled = false;
            showMessage(error.message);
        }
    });

    byId("problems")?.addEventListener("submit", async (event) => {
        const formElement = event.target.closest("[data-edit-problem]");
        if (!formElement) return;
        event.preventDefault();
        const form = new FormData(formElement);
        const submitButton = formElement.querySelector("[type=submit]");
        submitButton.disabled = true;
        try {
            await api(`/api/problems/${formElement.dataset.editProblem}`, {
                method: "PUT",
                body: JSON.stringify({
                    title: form.get("title"),
                    description: form.get("description"),
                    organizationId: Number(form.get("organizationId")),
                    categoryId: Number(form.get("categoryId")),
                    deadline: form.get("deadline"),
                    requiredSkillIds: JSON.parse(form.get("requiredSkillIds") || "[]"),
                    bountyAmount: Number(form.get("bountyAmount")),
                    offersEmployment: form.get("offersEmployment") === "on"
                })
            });
            await refresh();
            showMessage("Challenge updated.");
        } catch (error) {
            submitButton.disabled = false;
            showMessage(error.message);
        }
    });

    byId("proposalsList")?.addEventListener("change", async (event) => {
        const select = event.target.closest(".proposal-status-control");
        if (!select) return;
        try {
            await api(`/api/proposals/${select.dataset.proposalId}/status`, {
                method: "PATCH",
                body: JSON.stringify({ status: select.value })
            });
            await refresh();
            showMessage("Proposal stage updated.");
        } catch (error) {
            showMessage(error.message);
        }
    });
}

async function reloadDirectory() {
    lookups = await api("/api/lookups");
    renderDirectory();
    fillSelect(byId("categoryFilter"), lookups.categories, (item) => item.name, "All topics");
}

initialize().catch((error) => showMessage(error.message));
