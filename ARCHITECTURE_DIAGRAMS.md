# TrackChangesEngine Refactoring - Architecture Diagrams

## Class Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                                                                     │
│                  <<interface>>                                     │
│            IDbConnectionFactory                                   │
│  ┌────────────────────────────────────────────┐                  │
│  │ CreateConnectionAsync(CancellationToken)   │                  │
│  │    : Task<DbConnection>                    │                  │
│  └────────────────────────────────────────────┘                  │
│                                                                     │
└─────────────────┬──────────────────────────────────────────────────┘
				  │
				  │ <<implements>>
				  │
	┌─────────────┴─────────────┬──────────────────┐
	│                           │                  │
	│                           │                  │
	▼                           ▼                  ▼
┌──────────────────┐  ┌──────────────────┐  ┌─────────────────┐
│ Npgsql           │  │ Mock             │  │ Future: MySQL   │
│ Connection       │  │ DbConnection     │  │ Sqlite, etc.    │
│ Factory          │  │ Factory          │  │ (Pluggable)     │
│                  │  │                  │  │                 │
│ - host           │  │ (for testing)    │  │                 │
│ - port           │  │ - Mock mocks DB  │  │                 │
│ - username       │  │ - Fast tests     │  │                 │
│ - password       │  │ - No DB needed   │  │                 │
│ - database       │  │                  │  │                 │
│                  │  │                  │  │                 │
│ + CreateConnection       + CreateConnection                 │
│   Async()           Async()          │                 │
└──────────────────┘  └──────────────────┘  └─────────────────┘
	▲                           ▲                  ▲
	│ creates                   │ creates          │ creates
	│                           │                  │
	│                           │                  │
	│                           │                  │
	│                           │                  │
	│  ┌──────────────────────────────────────────────────────┐
	│  │                                                      │
	│  │    ┌─────────────────────────────────────────┐      │
	│  │    │   TrackChangesEngine                    │      │
	│  │    ├─────────────────────────────────────────┤      │
	│  │    │ - _ui: SynchronizationContext           │      │
	│  │    │ - _connectionFactory: IDbConnection...  │      │
	│  │    │ + OnChange: event                       │      │
	│  │    ├─────────────────────────────────────────┤      │
	│  │    │ + TrackChangesEngine(IDbConnectionFact) │      │
	│  │    │ + TrackAsync(parameters...)             │      │
	│  │    │                                         │      │
	│  │    │ [DEPRECATED]                            │      │
	│  │    │ + TrackAsync(legacy parameters...)      │      │
	│  │    └─────────────────────────────────────────┘      │
	│  │                                                      │
	│  └──────────────────────────────────────────────────────┘
	│                                                          │
	└──────────────────────────────────────────────────────────┘
			  uses dependency injection
```

## Sequence Diagram: Production Flow

```
┌─────────────────┐          ┌──────────────┐          ┌──────────────┐
│   Application   │          │   Engine     │          │   Factory    │
└────────┬────────┘          └───────┬──────┘          └──────┬───────┘
		 │                           │                        │
		 │ new NpgsqlConnection      │                        │
		 │ Factory(...)              │                        │
		 ├──────────────────────────>│                        │
		 │                           │                        │
		 │ new TrackChangesEngine    │                        │
		 │ (factory)                 │                        │
		 ├──────────────────────────>│                        │
		 │                           │                        │
		 │ TrackAsync(..., factory)  │                        │
		 ├──────────────────────────>│                        │
		 │                           │                        │
		 │                           │ CreateConnectionAsync  │
		 │                           ├───────────────────────>│
		 │                           │                        │
		 │                           │ builds connection      │
		 │                           │ string                 │
		 │                           │                        │
		 │                           │ new NpgsqlConnection() │
		 │                           │ openAsync()            │
		 │                           │<───────────────────────┤
		 │                           │                        │
		 │                           │ NpgsqlConnection       │
		 │                           │<───────────────────────┤
		 │                           │                        │
		 │                           │ cast to NpgsqlConnection
		 │                           │ use extension methods  │
		 │                           │ query database         │
		 │                           │                        │
		 │<──────────────────────────┤                        │
		 │                           │                        │
		 │ OnChange event fired      │                        │
		 │                           │                        │
```

## Sequence Diagram: Unit Test Flow

```
┌─────────────────┐          ┌──────────────┐          ┌──────────────┐
│   Unit Test     │          │   Engine     │          │ Mock Factory │
└────────┬────────┘          └───────┬──────┘          └──────┬───────┘
		 │                           │                        │
		 │ new MockDbConnectionFactory()                      │
		 ├──────────────────────────────────────────────────> │
		 │                           │                        │
		 │ new TrackChangesEngine(factory)                    │
		 ├──────────────────────────>│                        │
		 │                           │                        │
		 │ TrackAsync(..., factory)  │                        │
		 ├──────────────────────────>│                        │
		 │                           │                        │
		 │                           │ CreateConnectionAsync  │
		 │                           ├───────────────────────>│
		 │                           │                        │
		 │                           │ returns MockDb         │
		 │                           │ Connection             │
		 │                           │<───────────────────────┤
		 │                           │                        │
		 │                           │ cast to NpgsqlConnection
		 │                           │ (works - returns DbConnection base)
		 │                           │ execute mock commands  │
		 │                           │ (no real DB access!)   │
		 │                           │                        │
		 │<──────────────────────────┤                        │
		 │                           │                        │
		 │ Assert:                   │                        │
		 │ - Connection called       │                        │
		 │ - Commands executed       │                        │
		 │ - No database needed!     │                        │
		 │                           │                        │
```

## Dependency Injection Flow

```
Configuration/Setup
│
├─ Production Path
│  │
│  ├─ Read config (host, port, user, pwd, db)
│  │
│  ├─ new NpgsqlConnectionFactory(
│  │     host, port, user, pwd, db)
│  │
│  ├─ new TrackChangesEngine(factory)
│  │
│  └─ tracker.TrackAsync(..., factory, ct)
│     │
│     └─ factory.CreateConnectionAsync()
│        │
│        └─ new NpgsqlConnection()
│           await conn.OpenAsync()
│           │
│           └─ Real database connection ✅
│
└─ Test Path
   │
   ├─ new MockDbConnectionFactory()
   │
   ├─ new TrackChangesEngine(mockFactory)
   │
   └─ tracker.TrackAsync(..., mockFactory, ct)
	  │
	  └─ mockFactory.CreateConnectionAsync()
		 │
		 └─ new MockDbConnection()
			│
			└─ Fake connection ✅ (Fast, no DB needed)
```

## Backward Compatibility Flow

```
┌─────────────────────────────────────────────────────────────┐
│              LEGACY CODE (Old Way)                          │
│  var tracker = new TrackChangesEngine();                    │
│  await tracker.TrackAsync("table", "cols", "db", ...);      │
└────────────────────┬────────────────────────────────────────┘
					 │
					 │ [DEPRECATED WARNING]
					 │ This overload is obsolete
					 │
					 ▼
		┌──────────────────────────────┐
		│ TrackAsync(legacy params)    │
		│ [Obsolete Overload]          │
		└────────────┬─────────────────┘
					 │
					 │ Creates new factory internally
					 │ var factory = new NpgsqlConnectionFactory(...)
					 │
					 ▼
		┌──────────────────────────────────────┐
		│ TrackAsync(new params, factory)      │
		│ [Recommended Overload]               │
		└──────────────────────────────────────┘
					 │
					 │ Works exactly the same!
					 │ Zero breaking changes ✅

┌─────────────────────────────────────────────────────────────┐
│              NEW CODE (Recommended Way)                      │
│  var factory = new NpgsqlConnectionFactory(...);            │
│  var tracker = new TrackChangesEngine(factory);             │
│  await tracker.TrackAsync("table", "cols", ..., factory);   │
└─────────────────────────────────────────────────────────────┘
```

## Component Interaction Diagram

```
┌──────────────────────────────────────────────────────────────────┐
│                          Application                            │
└────────────────────────────┬─────────────────────────────────────┘
							 │
							 │ uses
							 ▼
				  ┌──────────────────────┐
				  │ TrackChangesEngine   │
				  │                      │
				  │ Core Business Logic  │
				  │ - Change tracking    │
				  │ - Event firing       │
				  │ - Data aggregation   │
				  └──────────┬───────────┘
							 │
							 │ depends on
							 ▼
				  ┌──────────────────────┐
				  │IDbConnectionFactory  │
				  │   (Abstraction)      │
				  └────────┬─────────────┘
						   │
			  ┌────────────┼────────────┬──────────────────┐
			  │            │            │                  │
			  ▼            ▼            ▼                  ▼
		┌──────────┐ ┌──────────┐ ┌──────────┐      ┌──────────────┐
		│Npgsql   │ │  Mock    │ │Sqlite   │      │ TestContainer│
		│Factory  │ │  Factory │ │Factory  │      │ (Future)     │
		└────┬────┘ └────┬─────┘ └────┬───┘      └──────┬───────┘
			 │           │            │                 │
			 │           │            │                 │
			 ▼           ▼            ▼                 ▼
		┌──────────┐ ┌──────────┐ ┌──────────┐  ┌──────────────┐
		│PostgreSQL│ │  Memory  │ │ SQLite   │  │   QuestDB    │
		│ QuestDB  │ │ (Test)   │ │ File     │  │ Container    │
		└──────────┘ └──────────┘ └──────────┘  └──────────────┘
```

## Test Execution Timeline

### Without Refactoring (Legacy)
```
Test Start
│
├─ Create TrackChangesEngine()
│
├─ Try to connect to 127.0.0.1:9000
│  │
│  ├─ [TIMEOUT - 30 seconds]
│  │
│  ├─ NpgsqlException thrown
│  │
│  └─ TEST FAILS ❌
│
└─ Test End: FAILED (took 30+ seconds)
```

### With Refactoring (New)
```
Test Start
│
├─ Create MockDbConnectionFactory()
│
├─ Create TrackChangesEngine(factory)
│
├─ Call TrackAsync(..., mockFactory, ct)
│  │
│  ├─ Mock factory returns mock connection
│  │
│  ├─ Execute mock commands (instant!)
│  │
│  ├─ No network access needed
│  │
│  ├─ No database required
│  │
│  └─ TEST PASSES ✅
│
└─ Test End: PASSED (< 100ms) ⚡
```

## Benefits Summary Diagram

```
					REFACTORED ARCHITECTURE
							│
			┌───────────────┼───────────────┐
			▼               ▼               ▼
	  ┌──────────┐   ┌──────────┐   ┌──────────┐
	  │Testability   │ Flexibility  │Performance
	  │─────────   │─────────── │─────────
	  │ ✅ Mocks   │ ✅ Any DB  │ ✅ <100ms
	  │ ✅ No DB   │ ✅ Plugins │ ✅ CI/CD
	  │ ✅ Fast    │ ✅ Future  │ ✅ Reliable
	  └──────────┘   └──────────┘   └──────────┘
			│               │               │
			└───────────────┼───────────────┘
							│
					┌───────┴────────┐
					▼                ▼
			  ┌──────────────┐  ┌──────────────┐
			  │ Unit Testing │  │ Maintainability
			  │──────────── │  │──────────────
			  │ ✅ Isolated  │  │ ✅ SOLID
			  │ ✅ Fast      │  │ ✅ Extensible
			  │ ✅ Reliable  │  │ ✅ Clean
			  └──────────────┘  └──────────────┘
```

## Migration Path Timeline

```
┌─────────┬──────────┬──────────┬──────────┐
│  Phase 1│  Phase 2 │  Phase 3 │  Phase 4 │
│ Current │Gradual   │ Complete │ Future  │
│ State   │Migration │ Migration│ Enhanc. │
└─────────┴──────────┴──────────┴──────────┘
	│         │          │         │
	│         │          │         │
Old & │   Code & │    All code  │   Support
New   │  tests   │    migrated  │   multiple
APIs  │  migrated├─ Remove      │   databases
work  │   ✅ New │   obsolete   │   Add
both  │   unit   │   API        │   TestContainers
	  │   tests  │   ✅ Clean   │   ✅ Mature
	  │   ✅ Mixed          code   ecosystem
	  │         API
```
