using Microsoft.Xrm.Sdk;
using System;

namespace BD.IPA.Plugins.Business
{
    /// <summary>
    /// Provides shared Dataverse services and execution state for typed business handlers.
    /// </summary>
    public abstract class BusinessBase<TEntity>
        where TEntity : Entity
    {
        public ILocalPluginContext Context { get; }

        public IOrganizationService Service => Context.PluginUserService;

        public IOrganizationService InitiatingUserService => Context.InitiatingUserService;

        public IOrganizationService AdminService => Context.AdminService;

        private readonly StateManager<TEntity> _stateManager;

        public TEntity Target => _stateManager.Target;
        public TEntity Initial => _stateManager.Initial;
        public TEntity Latest => _stateManager.Latest;

        public BusinessBase(ILocalPluginContext context, StateManager<TEntity> stateManager)
        {
            Context = context;
            _stateManager = stateManager;
        }

        public virtual void Execute()
        {
            var className = GetType().FullName;
            var start = DateTime.UtcNow;
            try
            {
                HandleExecute();
            }
            finally
            {
                var end = DateTime.UtcNow;

                Context.Trace($"PERFORMANCE {className}. Start: {start}. End: {end}. Total Seconds: {(end - start).TotalSeconds}..");
            }
        }

        public abstract void HandleExecute();
    }

    /// <summary>
    /// Provides shared Dataverse services and execution state for business handlers.
    /// </summary>
    public abstract class BusinessBase
    {
        public ILocalPluginContext Context { get; }

        public IOrganizationService Service => Context.PluginUserService;

        public IOrganizationService InitiatingUserService => Context.InitiatingUserService;

        public IOrganizationService AdminService => Context.AdminService;

        private readonly StateManager<Entity> _stateManager;

        public Entity Target => _stateManager.Target;
        public Entity Initial => _stateManager.Initial;
        public Entity Latest => _stateManager.Latest;

        public BusinessBase(ILocalPluginContext context, StateManager<Entity> stateManager)
        {
            Context = context;
            _stateManager = stateManager;
        }

        public virtual void Execute()
        {
            HandleExecute();
        }

        public abstract void HandleExecute();
    }
}
