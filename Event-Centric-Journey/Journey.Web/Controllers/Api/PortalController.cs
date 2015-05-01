﻿using Journey.Worker;
using System.Web.Http;

namespace Journey.Web.Controllers.Api
{
    public class PortalController : ApiController
    {
        private readonly IWorkerRoleWebPortal portal;

        public PortalController(IWorkerRoleWebPortal portal)
        {
            this.portal = portal;
        }

        [HttpGet]
        [Route("api/portal/status")]
        public IHttpActionResult Status()
        {
            return this.Ok(this.portal.IsWorking);
        }

        [HttpGet]
        [Route("api/portal/start")]
        public IHttpActionResult Start()
        {
            this.portal.StartWorking();
            return this.Ok(this.portal.IsWorking);
        }

        [HttpGet]
        [Route("api/portal/stop")]
        public IHttpActionResult Stop()
        {
            this.portal.StopWorking();
            return this.Ok(this.portal.IsWorking);
        }

        [HttpGet]
        [Route("api/portal/rebuildReadModel")]
        public IHttpActionResult RebuildReadModel()
        {
            this.portal.RebuildReadModel();
            return this.Ok(this.portal.IsWorking);
        }

        [HttpGet]
        [Route("api/portal/rebuildEventStore")]
        public IHttpActionResult RebuildEventStore()
        {
            this.portal.RebuildEventStore();
            return this.Ok(this.portal.IsWorking);
        }

        [HttpGet]
        [Route("api/portal/rebuildEventStoreAndReadModel")]
        public IHttpActionResult RebuildEventStoreAndReadModel()
        {
            this.portal.RebuildEventStoreAndReadModel();
            return this.Ok(this.portal.IsWorking);
        }
    }
}
