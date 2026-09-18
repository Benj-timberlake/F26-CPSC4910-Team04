# make          build, run BackEnd + FrontEnd, open the app
# make stop     stop both
# make test     run the tests
# make logs     tail both logs
# make status   what's running

SLN          := TruckerReward/TruckerReward.sln
BACKEND_DIR  := TruckerReward/BackEnd
FRONTEND_DIR := TruckerReward/FrontEnd
BACKEND_URL  := http://localhost:8080
FRONTEND_URL := http://localhost:8081
RUN_DIR      := .run

OPEN := $(if $(filter Darwin,$(shell uname)),open,xdg-open)

.PHONY: up build run wait open stop logs test status

up: build run wait open

build:
	dotnet build $(SLN)

run:
	@mkdir -p $(RUN_DIR)
	@$(MAKE) --no-print-directory stop
	@(cd $(BACKEND_DIR)  && exec env ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build) > $(RUN_DIR)/backend.log  2>&1 < /dev/null & echo $$! > $(RUN_DIR)/backend.pid
	@(cd $(FRONTEND_DIR) && exec env ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build) > $(RUN_DIR)/frontend.log 2>&1 < /dev/null & echo $$! > $(RUN_DIR)/frontend.pid

wait:
	@for i in $$(seq 1 60); do curl -fs $(BACKEND_URL)/health >/dev/null 2>&1 && break; sleep 1; done
	@for i in $$(seq 1 60); do curl -fs $(FRONTEND_URL)/login >/dev/null 2>&1 && break; sleep 1; done

open:
	$(OPEN) $(FRONTEND_URL)/login

stop:
	@for p in backend frontend; do \
	  if [ -f $(RUN_DIR)/$$p.pid ]; then pkill -P $$(cat $(RUN_DIR)/$$p.pid) 2>/dev/null; kill $$(cat $(RUN_DIR)/$$p.pid) 2>/dev/null; rm -f $(RUN_DIR)/$$p.pid; fi; \
	done
	@lsof -ti tcp:8080 -ti tcp:8081 2>/dev/null | xargs kill 2>/dev/null || true

logs:
	tail -n 50 -f $(RUN_DIR)/backend.log $(RUN_DIR)/frontend.log

test:
	dotnet test $(SLN)

status:
	@echo "backend : $$(curl -fs $(BACKEND_URL)/health >/dev/null 2>&1 && echo up || echo down)  $(BACKEND_URL)"
	@echo "frontend: $$(curl -fs $(FRONTEND_URL)/login >/dev/null 2>&1 && echo up || echo down)  $(FRONTEND_URL)"
