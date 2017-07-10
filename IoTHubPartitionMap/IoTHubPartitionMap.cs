using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Actors;
using Microsoft.ServiceFabric.Actors.Runtime;
using Microsoft.ServiceFabric.Actors.Client;
using IoTHubPartitionMap.Interfaces;
using System.Runtime.Serialization;
using Microsoft.ServiceBus.Messaging;

namespace IoTHubPartitionMap
{
    /// <remarks>
    /// This class represents an actor.
    /// Every ActorID maps to an instance of this class.
    /// The StatePersistence attribute determines persistence and replication of actor state:
    ///  - Persisted: State is written to disk and replicated.
    ///  - Volatile: State is kept in memory only and replicated.
    ///  - None: State is kept in memory only and not replicated.
    /// </remarks>
    [StatePersistence(StatePersistence.Persisted)]
    internal class IoTHubPartitionMap : Actor, IIoTHubPartitionMap
    {
        IActorTimer mTimer;
        [DataContract]
        internal sealed class ActorState
        {
            [DataMember]
            public List<string> PartitionNames { get; set; }
            [DataMember]
            public Dictionary<string, DateTime> PartitionLeases { get; set; }
        }

        /// <summary>
        /// Initializes a new instance of IoTHubPartitionMap
        /// </summary>
        /// <param name="actorService">The Microsoft.ServiceFabric.Actors.Runtime.ActorService that will host this actor instance.</param>
        /// <param name="actorId">The Microsoft.ServiceFabric.Actors.ActorId for this actor instance.</param>
        public IoTHubPartitionMap(ActorService actorService, ActorId actorId)
            : base(actorService, actorId)
        {
        }

        /// <summary>
        /// This method is called whenever an actor is activated.
        /// An actor is activated the first time any of its methods are invoked.
        /// </summary>
        protected override Task OnActivateAsync()
        {
            ActorEventSource.Current.ActorMessage(this, "Actor activated.");

            // The StateManager is this actor's private state store.
            // Data stored in the StateManager will be replicated for high-availability for actors that use volatile or persisted state storage.
            // Any serializable object can be saved in the StateManager.
            // For more information, see https://aka.ms/servicefabricactorsstateserialization

            // return this.StateManager.TryAddStateAsync("count", 0);
            // ResetPartitionNames();
            mTimer = RegisterTimer(CheckLease, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            this.StateManager.TryAddStateAsync("mystate", new ActorState {
                PartitionNames = new List<string>(),
                PartitionLeases = new Dictionary<string, DateTime>()
            });
            return Task.FromResult(true);
        }
        private async Task CheckLease(Object state)
        {
            await ResetPartitionNames();

            ActorState mystate = await this.StateManager.GetStateAsync<ActorState>("mystate");
            List<string> keys = mystate.PartitionLeases.Keys.ToList();
            foreach(string key in keys)
            {
                if (DateTime.Now - mystate.PartitionLeases[key] >= TimeSpan.FromSeconds(60)) mystate.PartitionLeases.Remove(key);
            }
            return ;
        }
        protected override Task OnDeactivateAsync()
        {
            if (mTimer != null) UnregisterTimer(mTimer);
            return base.OnDeactivateAsync();
        }

        async Task<string> IIoTHubPartitionMap.LeaseIoTHubPartitionAsync()
        {
            string ret = "";
            ActorState mystate = await this.StateManager.GetStateAsync<ActorState>("mystate");
            foreach (string partition in mystate.PartitionNames)
            {
                if (mystate.PartitionLeases.ContainsKey(partition))
                {
                    mystate.PartitionLeases.Add(partition, DateTime.Now);
                    ret = partition;
                    break;
                }
            }
            
            return (ret);
        }
        async Task<string> IIoTHubPartitionMap.RenewIoTHubPartitionLeaseAsync(string partiton)
        {
            ActorState mystate = await this.StateManager.GetStateAsync<ActorState>("mystate");
            string ret = "";
            if (mystate.PartitionLeases.ContainsKey(partiton))
            {
                mystate.PartitionLeases[partiton] = DateTime.Now;
                ret = partiton;
            }
            return ret;
        }
        async Task ResetPartitionNames()
        {
            ActorState mystate = await this.StateManager.GetStateAsync<ActorState>("mystate");
            var eventHubClient = EventHubClient.CreateFromConnectionString("HostName=IoTHubSimulation.azure-devices.net;SharedAccessKeyName=iothubowner;SharedAccessKey=hGMWqAtssRJP9XstublnxBakhEn9PwX9D2gclsWbLWM=", "message/events");
            var partitions = eventHubClient.GetRuntimeInformation().PartitionIds;
            foreach(string partition in partitions)
            {
                mystate.PartitionNames.Add(partition);
            }
        }
        /// <summary>
        /// TODO: Replace with your own actor method.
        /// </summary>
        /// <returns></returns>
        Task<int> IIoTHubPartitionMap.GetCountAsync(CancellationToken cancellationToken)
        {
            return this.StateManager.GetStateAsync<int>("count", cancellationToken);
        }

        /// <summary>
        /// TODO: Replace with your own actor method.
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        Task IIoTHubPartitionMap.SetCountAsync(int count, CancellationToken cancellationToken)
        {
            // Requests are not guaranteed to be processed in order nor at most once.
            // The update function here verifies that the incoming count is greater than the current count to preserve order.
            return this.StateManager.AddOrUpdateStateAsync("count", count, (key, value) => count > value ? count : value, cancellationToken);
        }
    }
}
