# 🎮 MMO Game Monitoring System - UDP Protocol

High-performance multiplayer game server with real-time monitoring, now **UDP-only** for optimal latency and throughput.

## 🚀 Quick Start

### Backend (Docker)
```bash
cd Backend
docker-compose up --build
```

### Unity Client Setup
⚠️ **IMPORTANT**: Server is now UDP-only. See [Unity UDP Setup Guide](UnityClient/SETUP_UDP_CLIENT.md)

## 🌐 Server Endpoints

- **Game Protocol**: UDP port 8081
- **Health Check**: http://localhost:8080/health  
- **Status Page**: http://localhost:8080/admin

## 📊 Performance Features

### UDP Protocol Benefits
- ⚡ **Ultra-Low Latency**: ~50% faster than WebSocket/TCP
- 🚀 **High Throughput**: Supports 100+ concurrent entities
- 📦 **Efficient Packets**: Minimal bandwidth usage
- 🎯 **Game-Optimized**: Purpose-built for real-time gaming

### Server Capabilities
- ✅ **Server-Authoritative Movement** with client-side prediction
- ✅ **Real-time State Synchronization** (20Hz)
- ✅ **Adaptive Broadcast Throttling** for high entity counts
- ✅ **Bot Management System** for load testing
- ✅ **Performance Metrics** and monitoring
- ✅ **Docker Containerized** deployment

## 🏗️ Architecture

```
Unity Client (UDP) ←→ UDP Server (Port 8081) ←→ Game Session
                           ↓
                    HTTP API (Port 8080)
                           ↓
                    Status & Monitoring
```

## 🔧 Technical Stack

- **Backend**: .NET 8, ASP.NET Core, UDP Sockets
- **Frontend**: Unity 2022.3+, C# UDP Client
- **Infrastructure**: Docker, Docker Compose
- **Protocol**: UDP for game traffic, HTTP for status

## 📈 Monitoring & Metrics

Real-time server metrics available at `/admin`:
- Total entities and players
- Bot counts and behavior states  
- Memory usage and update rates
- Connection status and health

## 🎮 Game Features

- **Real-time Movement**: Server-authoritative with client prediction
- **Multi-Entity Support**: Optimized for 100+ concurrent entities
- **Bot System**: AI bots for testing and load simulation
- **Reconciliation**: Smooth correction of client prediction errors
- **Performance Scaling**: Adaptive systems for high entity counts

---

*🔥 **Migration Complete**: WebSocket support removed for UDP-only performance optimization*
