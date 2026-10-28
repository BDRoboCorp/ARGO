using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace BD.IPA.Plugins.Business
{
    /// <summary>
    /// Tracks the target, initial, and latest entity states for a plugin execution.
    /// </summary>
    public class StateManager<TEntity>
        where TEntity : Entity
    {
        private readonly ILocalPluginContext _localPluginContext;
        private readonly ColumnSet _columnSet;

        public StateManager(ILocalPluginContext localPluginContext, ColumnSet columnSet = null)
        {
            _localPluginContext = localPluginContext;
            _columnSet = columnSet ?? new ColumnSet(true);
        }

        private IPluginExecutionContext _pluginExecutionContext;
        private IPluginExecutionContext PluginExecutionContext
        {
            get
            {
                if (_pluginExecutionContext == null)
                {
                    _pluginExecutionContext = _localPluginContext.PluginExecutionContext;
                }
                return _pluginExecutionContext;
            }
        }

        private TEntity _target;
        public TEntity Target
        {
            get
            {
                if (_target == null)
                {
                    _target = GetTarget().ToEntity<TEntity>();
                }
                return _target;
            }
        }

        private Entity GetTarget()
        {
            switch (PluginExecutionContext.MessageName)
            {
                case "Create":
                case "Update":
                    var target = (Entity)PluginExecutionContext.InputParameters["Target"];
                    return target;
                case "Delete":
                    var entityRef = (EntityReference)PluginExecutionContext.InputParameters["Target"];
                    var entity = _localPluginContext.PluginUserService.Retrieve(entityRef.LogicalName, entityRef.Id, _columnSet);
                    return entity;
                default:
                    throw new InvalidPluginExecutionException($"Target accessor for {PluginExecutionContext.MessageName} not supported.");
            }
        }

        private TEntity _initial;
        public TEntity Initial
        {
            get
            {
                if (_initial == null)
                {
                    _initial = PluginExecutionContext.MessageName == "Create" ? Target :
                        (GetFirstImageEntity() ?? _localPluginContext.InitiatingUserService.Retrieve(Target.LogicalName, Target.Id, _columnSet).ToEntity<TEntity>());
                }
                return _initial;
            }
        }

        public TEntity Latest
        {
            get
            {
                var temp = Activator.CreateInstance<TEntity>();
                temp.Id = Target.Id;

                foreach (var targetAttribute in Initial.Attributes)
                {
                    temp[targetAttribute.Key] = targetAttribute.Value;
                }

                foreach (var targetAttribute in Initial.FormattedValues)
                {
                    temp.FormattedValues[targetAttribute.Key] = targetAttribute.Value;
                }

                foreach (var targetAttribute in Target.Attributes)
                {
                    temp[targetAttribute.Key] = targetAttribute.Value;
                }

                foreach (var targetAttribute in Target.FormattedValues)
                {
                    temp.FormattedValues[targetAttribute.Key] = targetAttribute.Value;
                }

                return temp;
            }
        }

        private TEntity GetFirstImageEntity()
        {
            var images = PluginExecutionContext.Mode == 40 ? PluginExecutionContext.PostEntityImages : PluginExecutionContext.PreEntityImages;
            foreach (var image in images)
            {
                return image.Value.ToEntity<TEntity>();
            }

            return null;
        }
    }
}
