const store = {
  get mock() { try { return localStorage.getItem('at.mock') !== '0'; } catch { return true; } },
  set mock(v) { try { localStorage.setItem('at.mock', v ? '1' : '0'); } catch { /* noop */ } },
  /* true once the user saves an explicit source choice in ⚙ Settings —
     boot-time auto-detection only runs while this is false. */
  get mockSet() { try { return localStorage.getItem('at.mockSet') === '1'; } catch { return false; } },
  set mockSet(v) { try { localStorage.setItem('at.mockSet', v ? '1' : '0'); } catch { /* noop */ } },
  get apiBase() { try { return localStorage.getItem('at.apiBase') ?? '/api'; } catch { return '/api'; } },
  set apiBase(v) { try { localStorage.setItem('at.apiBase', v); } catch { /* noop */ } },
};

export const CONFIG = {
  get mock() { return store.mock; },
  set mock(v) { store.mock = v; },
  get mockSet() { return store.mockSet; },
  set mockSet(v) { store.mockSet = v; },
  get apiBase() { return store.apiBase; },
  set apiBase(v) { store.apiBase = v.replace(/\/+$/, ''); },

  APP_VERSION: '0.1.0',
  BUILD: 'frontend-phase-a',

  /* Future REST endpoints (ASP.NET Core API contract — Phase 8):
     GET  /health
     GET  /datasets            POST /datasets/import
     GET  /datasets/:id        GET  /datasets/:id/candles?page=&size=
     GET  /datasets/:id/rangestats?from=&to=
     GET  /datasets/:id/pricelevels?limit=
     GET  /strategies          POST /strategies        DELETE /strategies/:id
     GET  /runs                POST /runs              GET /runs/:id
     POST /backtest            (SSE / progress endpoint TBD)
     GET  /benchmark?algo=&size=
     GET  /live/candles?symbol=&tf=
  */
};
