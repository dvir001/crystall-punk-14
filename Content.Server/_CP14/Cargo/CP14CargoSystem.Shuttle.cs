using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._CP14.Cargo;
using Content.Shared.Gravity;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._CP14.Cargo;

public sealed partial class CP14CargoSystem
{
    private void InitializeShuttle()
    {
        SubscribeLocalEvent<CP14TravelingStoreShipComponent, FTLCompletedEvent>(OnFTLCompleted);
        SubscribeLocalEvent<CP14TravelingStoreShipComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<CP14TravelingStoreShipComponent> ent, ref MapInitEvent args)
    {
        //TODO: This is shitcode! Because shouldnt related to traveling ship
        EnsureComp<GravityComponent>(ent, out var gravity);
        gravity.Enabled = true;
        gravity.Inherent = true;
    }

    private void UpdateShuttle()
    {
        var query = EntityQueryEnumerator<CP14StationTravelingStoreShipTargetComponent>();
        while (query.MoveNext(out var uid, out var ship))
        {
            if (_timing.CurTime < ship.NextTravelTime || ship.NextTravelTime == TimeSpan.Zero)
                continue;

            if (ship.Shuttle is null || ship.TradePostMap is null)
                continue;

            if (Transform(ship.Shuttle.Value).MapUid == Transform(ship.TradePostMap.Value).MapUid)
            {
                // if landed on trade post
                ship.NextTravelTime = _timing.CurTime + ship.StationWaitTime;
                SendShuttleToStation(ship.Shuttle.Value);
            }
            else
            {
                // if landed on station
                ship.NextTravelTime = _timing.CurTime + ship.TradePostWaitTime;
                SendShuttleToTradepost(ship.Shuttle.Value, ship.TradePostMap.Value);
            }
        }
    }

    private void SendShuttleToStation(EntityUid shuttle, float startupTime = 0f)
    {
        var targetPoints = new List<EntityUid>();
        var targetEnumerator =
            EntityQueryEnumerator<CP14TravelingStoreShipFTLTargetComponent,
                TransformComponent>(); //TODO - different method position location
        while (targetEnumerator.MoveNext(out var uid, out _, out _))
        {
            targetPoints.Add(uid);
        }

        if (targetPoints.Count == 0)
            return;

        var target = _random.Pick(targetPoints);
        var targetXform = Transform(target);

        var shuttleComp = Comp<ShuttleComponent>(shuttle);

        _shuttles.FTLToCoordinates(shuttle,
            shuttleComp,
            targetXform.Coordinates,
            targetXform.LocalRotation,
            hyperspaceTime: 20f,
            startupTime: startupTime);
    }

    private void SendShuttleToTradepost(EntityUid shuttle, EntityUid tradePostMap)
    {
        var shuttleComp = Comp<ShuttleComponent>(shuttle);

        _shuttles.FTLToCoordinates(shuttle,
            shuttleComp,
            new EntityCoordinates(tradePostMap, Vector2.Zero),
            Angle.Zero,
            startupTime: 10f,
            hyperspaceTime: 20f);
    }

    private void OnFTLCompleted(Entity<CP14TravelingStoreShipComponent> ent, ref FTLCompletedEvent args)
    {
        if (!TryComp<CP14StationTravelingStoreShipTargetComponent>(ent.Comp.Station, out var station))
            return;

        if (station.TradePostMap is not null &&
            Transform(ent).MapUid == Transform(station.TradePostMap.Value).MapUid) //Landed on tradepost
        {
            station.OnStation = false;

            SellingThings((ent.Comp.Station, station)); // +balance
            TopUpBalance((ent.Comp.Station, station)); //+balance
            BuyToQueue((ent.Comp.Station, station)); //-balance +buyQueue
            TrySpawnBuyedThings((ent.Comp.Station, station));
            UpdateStorePositions((ent.Comp.Station, station));
        }
        else //Landed on station
        {
            station.OnStation = true;

            CashOut((ent.Comp.Station, station));
            station.Balance = 0;
        }

        UpdateAllStores();
    }
}
