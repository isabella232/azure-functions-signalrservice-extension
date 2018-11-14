﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs.Extensions.SignalRService.Protocols;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Microsoft.Azure.WebJobs.Extensions.SignalRService
{
    // A app scoped singleton dispatcher to diliver EventData to correct functions
    public class SignalRTriggerListenerDispatcher
    {
        private readonly ConcurrentDictionary<Type, HashSet<SignalRTriggerFunctionData>> _arrtibuteDictionary =
            new ConcurrentDictionary<Type, HashSet<SignalRTriggerFunctionData>>();

        private readonly ISignalRExtensionProtocols _protocols;

        public SignalRTriggerListenerDispatcher(ISignalRExtensionProtocols protocols)
        {
            _protocols = protocols;
        }

        public void RegisterFunction(string functionId, Type attributeType, string hubName, ListenerFactoryContext context, string target = null)
        {
            _arrtibuteDictionary.AddOrUpdate(attributeType, type => new HashSet<SignalRTriggerFunctionData> {new SignalRTriggerFunctionData(context)
            {
                Hub = hubName,
                Target = target

            }}, (type, set) =>
            {
                set.Add(new SignalRTriggerFunctionData(context)
                {
                    Hub = hubName,
                    Target = target
                });
                return set;
            });
        }

        public async Task DispatchListener(EventData input, CancellationTokenSource cts)
        {
            HashSet<SignalRTriggerFunctionData> relatedFunctionsByType = null;

            if (!_protocols.TryParseMessage(input, out var message))
            {
                return;
            }

            switch (message.MessageType)
            {
                case SignalRExtensionProtocolConstants.OpenConnectionType:
                    _arrtibuteDictionary.TryGetValue(typeof(SignalROpenConnectionTriggerAttribute),
                        out relatedFunctionsByType);
                    break;
                case SignalRExtensionProtocolConstants.CloseConnectionType:
                    _arrtibuteDictionary.TryGetValue(typeof(SignalRCloseConnectionTriggerAttribute),
                        out relatedFunctionsByType);
                    break;
                case SignalRExtensionProtocolConstants.InvocationType:
                    _arrtibuteDictionary.TryGetValue(typeof(SignalRInvocationMessageTriggerAttribute),
                        out relatedFunctionsByType);
                    break;
            }

            if (relatedFunctionsByType == null)
            {
                return;
            }

            var relatedFunctions = relatedFunctionsByType.Where(data => SignalRTriggerFunctionData.Filter(data, message));

            foreach (var functionData in relatedFunctions)
            {
                var context = functionData.Context;

                if (context != null)
                {
                    var triggeredInput = new TriggeredFunctionData()
                    {
                        TriggerValue = message
                    };
                    await context.Executor.TryExecuteAsync(triggeredInput, cts.Token);
                }
            }
        }
    }
}
