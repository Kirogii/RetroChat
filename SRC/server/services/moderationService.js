const axios = require('axios');

const Env = require('../config/env');

// Using Perspective API to filter messages
// See: https://developers.perspectiveapi.com/s/about-the-api?language=en_US
// WARNING: Perspective API currently has a 1 QPS limit on free tier

// Perspective users must register for access
// See: https://developers.perspectiveapi.com/s/docs-get-started?language=en_US

// Map attributes
// See: https://developers.perspectiveapi.com/s/about-the-api-attributes-and-languages?language=en_US
const ATTRIBUTES = {
    TOXICITY: {},
    INSULT: {},
    PROFANITY: {},
    SEVERE_TOXICITY: {},
    IDENTITY_ATTACK: {},
    THREAT: {},
    SEXUALLY_EXPLICIT: {} // Experimental attribute
};

// Offline fallback used when no Perspective key is configured. This deliberately
// permits ordinary profanity while retaining basic protection against direct threats
// and severe targeted abuse. It is intentionally conservative and dependency-free.
function applyLocalSafetyPolicy(text) {
    const normalized = String(text || '').toLowerCase().replace(/[^a-z0-9\s']/g, ' ');
    const directThreat = /\b(?:i(?:'ll| will| am going to|m gonna)|we(?:'ll| will| are going to|re gonna))\s+(?:kill|murder|shoot|stab|bomb|dox|swat)\s+(?:you|your|them|him|her)\b/;
    const severeWish = /\b(?:go|you should)\s+(?:kill|hang)\s+yourself\b/;
    const credentialTheft = /\b(?:send|give|show|tell)\s+(?:me\s+)?(?:your\s+)?(?:password|login code|auth code|token)\b/;

    if (directThreat.test(normalized) || severeWish.test(normalized) || credentialTheft.test(normalized)) {
        return { allowed: false, reason: 'moderation', attributeScores: null };
    }
    return { allowed: true, attributeScores: null };
}

// See: https://developers.perspectiveapi.com/s/about-the-api-methods?language=en_US
async function isMessageAllowed(text, options = {}) {
    if (!Env.PERSPECTIVE_API_KEY) {
        return applyLocalSafetyPolicy(text);
    }

    try {
        const payload = {
            comment: { text, type: "PLAIN_TEXT" },
            requestedAttributes: ATTRIBUTES,
            languages: ["en"],
            doNotStore: true // Important: Tells Google not to store chat logs for training
        };

        const response = await axios.post(
            `https://commentanalyzer.googleapis.com/v1alpha1/comments:analyze?key=${Env.PERSPECTIVE_API_KEY}`,
            payload,
            { timeout: 10000 }
        );

        console.dir(response.data, { depth: null }); // Print full response for debugging

        const scores = response.data.attributeScores;

        // === Custom policy logic ===
        // Block severe toxicity, threats, sexual explicit, etc.
        // The client will locally filter more strictly based on user preferences
        const ordinaryLanguageBlocked = !options.allowProfanity && (
            (scores.TOXICITY?.summaryScore?.value || 0) > 0.80 ||
            (scores.INSULT?.summaryScore?.value || 0) > 0.80 ||
            (scores.PROFANITY?.summaryScore?.value || 0) > 0.95
        );

        if (
            (scores.IDENTITY_ATTACK?.summaryScore?.value || 0) > 0.50 ||
            (scores.SEXUALLY_EXPLICIT?.summaryScore?.value || 0) > 0.60 ||
            (scores.SEVERE_TOXICITY?.summaryScore?.value || 0) > 0.60 ||
            (scores.THREAT?.summaryScore?.value || 0) > 0.70 ||
            ordinaryLanguageBlocked
        ) {
            return {
                allowed: false,
                reason: 'moderation'
            };
        }

        // Everything else is allowed
        return {
            allowed: true,
            attributeScores: scores
        };
    } catch (err) {
        console.error("Perspective API error:", err.message);
        return {
            allowed: false,
            reason: 'api_error'
        }; // Fail closed
    }
}

module.exports = { isMessageAllowed };
