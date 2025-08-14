# Backend (WebSocket demo)

Servidor muy básico autoritativo que mantiene una única sesión y difunde el estado de todos los jugadores conectados.

## Endpoints
- `GET /health` -> { status: "ok" }
- `WS /ws` -> Protocolo JSON simple

Mensajes cliente -> servidor:
```jsonc
{ "op": "join", "name": "PlayerName" }
{ "op": "move", "x": 0.0, "y": 0.0, "z": 0.0 }
```

Respuestas servidor:
```jsonc
{ "op": "join_ack", "id": "GUID" }
{ "op": "state", "players": [ { "id": "GUID", "name": "Player", "x":0, "y":0, "z":0 } ] }
```

## Ejecutar con Docker
```powershell
cd Backend
docker compose up --build -d
docker compose logs -f backend
```

WebSocket URL: `ws://localhost:8080/ws`

## Desarrollo local (sin Docker)
```powershell
cd Backend/src
dotnet run
```

## Próximos pasos sugeridos
- Validar nombre y limitar rate de mensajes `move`.
- Interpolación de posiciones en el cliente.
- Gestión de múltiples salas (mapa: roomId -> GameSession).
- Autenticación básica (token en query string al conectar).
