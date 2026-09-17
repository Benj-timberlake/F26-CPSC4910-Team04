# Builds main, starts MySQL + BackEnd + FrontEnd, and opens the app.
#
#   make            checkout branch, build, run everything, open browser tabs
#   make stop       stop the two dotnet servers and the mysql container
#   make logs       tail backend + frontend logs
#   make test       run the xunit tests
#   make status     what's running + urls
#   make clean      stop everything and wipe the mysql volume
#
#   make BRANCH=x   to use a different branch

BRANCH       ?= main
SLN          := TruckerReward/SampleApp.sln
BACKEND_DIR  := TruckerReward/BackEnd
FRONTEND_DIR := TruckerReward/FrontEnd
BACKEND_URL  := http://localhost:8080
FRONTEND_URL := http://localhost:8081
RUN_DIR      := .run

OPEN := $(if $(filter Darwin,$(shell uname)),open,xdg-open)

.PHONY: up checkout db build run wait open stop logs test status clean

up: checkout db build run wait open
	@echo
	@echo "  FrontEnd : $(FRONTEND_URL)/register"
	@echo "  BackEnd  : $(BACKEND_URL)/scalar"
	@echo "  logs     : make logs      stop: make stop"

checkout:
	@if [ -n "$$(git status --porcelain --untracked-files=no)" ]; then \
	  echo ">> working tree has changes, staying on $$(git branch --show-current)"; \
	else \
	  git checkout $(BRANCH) && git pull --ff-only || true; \
	fi

db:
	docker compose up -d
	@echo ">> waiting for mysql..."
	@for i in $$(seq 1 60); do \
	  docker compose exec -T mysql mysqladmin ping -uroot -proot --silent >/dev/null 2>&1 && break; \
	  sleep 1; \
	done

build:
	dotnet build $(SLN)

run: $(RUN_DIR)
	@$(MAKE) --no-print-directory stop-dotnet
	@(cd $(BACKEND_DIR)  && exec env ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build) > $(RUN_DIR)/backend.log  2>&1 < /dev/null & echo $$! > $(RUN_DIR)/backend.pid
	@(cd $(FRONTEND_DIR) && exec env ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build) > $(RUN_DIR)/frontend.log 2>&1 < /dev/null & echo $$! > $(RUN_DIR)/frontend.pid
	@echo ">> backend pid $$(cat $(RUN_DIR)/backend.pid), frontend pid $$(cat $(RUN_DIR)/frontend.pid)"

$(RUN_DIR):
	@mkdir -p $(RUN_DIR)

wait:
	@echo ">> waiting for backend..."
	@for i in $$(seq 1 60); do curl -fs $(BACKEND_URL)/health >/dev/null 2>&1 && break; sleep 1; done
	@echo ">> waiting for frontend..."
	@for i in $$(seq 1 60); do curl -fs $(FRONTEND_URL)/register >/dev/null 2>&1 && break; sleep 1; done

open:
	$(OPEN) $(BACKEND_URL)/scalar
	$(OPEN) $(FRONTEND_URL)/login
	$(OPEN) $(FRONTEND_URL)/register

stop-dotnet:
	@for p in backend frontend; do \
	  if [ -f $(RUN_DIR)/$$p.pid ]; then pkill -P $$(cat $(RUN_DIR)/$$p.pid) 2>/dev/null; kill $$(cat $(RUN_DIR)/$$p.pid) 2>/dev/null; rm -f $(RUN_DIR)/$$p.pid; fi; \
	done
	@lsof -ti tcp:8080 -ti tcp:8081 2>/dev/null | xargs kill 2>/dev/null || true

stop: stop-dotnet
	docker compose stop

logs:
	tail -n 50 -f $(RUN_DIR)/backend.log $(RUN_DIR)/frontend.log

test:
	dotnet test $(SLN)

status:
	@echo "branch  : $$(git branch --show-current)"
	@echo "mysql   : $$(docker compose ps --status running -q mysql 2>/dev/null | grep -q . && echo running || echo stopped)"
	@echo "backend : $$(curl -fs $(BACKEND_URL)/health >/dev/null 2>&1 && echo up at $(BACKEND_URL)/scalar || echo down)"
	@echo "frontend: $$(curl -fs $(FRONTEND_URL)/register >/dev/null 2>&1 && echo up at $(FRONTEND_URL)/register || echo down)"

clean: stop-dotnet
	docker compose down -v
	rm -rf $(RUN_DIR)
