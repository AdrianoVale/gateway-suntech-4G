// ── WebSocket Hub (SignalR) ────────────────────────────────────────────────────
// Segredo HS256 compartilhado com Gateway:Hub:JwtSecret em appsettings.json
if (!defined('WS_JWT_SECRET')) {
    define('WS_JWT_SECRET', 'blt-ws-hub-secret-2026-sunteh-prod');
}
if (!defined('WS_HUB_URL')) {
    define('WS_HUB_URL', '/hub/posicoes');
}
