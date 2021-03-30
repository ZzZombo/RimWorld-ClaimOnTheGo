using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimWorld;
using System.Linq;

namespace ZzZomboRW
{
    public class CompProperties_Claimable : CompProperties
    {
        public int range = 3;
        public bool enabled = true;
        public bool byPlayer = true;
        public bool byEnemies = true;
        public CompProperties_Claimable()
        {
            this.compClass = typeof(CompClaimable);
        }
    }
    public class CompClaimable : ThingComp
    {
        public CompProperties_Claimable Props => (CompProperties_Claimable)this.props;
        public bool Enabled => this.parent.Spawned && this.Props.enabled &&
            this.Props.range > 0 && (this.Props.byEnemies || this.Props.byPlayer) && this.parent is Building;
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            Log.Message($"[ZzZomboRW.CompClaimable] Initialized ({this.parent}).\nCapture by enemies: {this.Props.byEnemies};\nCapture by player: {this.Props.byPlayer};\nEnabled: {this.Props.enabled};\nRange: {this.Props.range};\n" +
                $"Ticker: {this.parent.def.tickerType}.");
            if (this.parent.def.tickerType == TickerType.Never)
            {
                this.parent.def.tickerType = TickerType.Rare;
            }
        }
        public override void PostExposeData()
        {
            Scribe_Values.Look(ref this.Props.byEnemies, "byEnemies", true, false);
            Scribe_Values.Look(ref this.Props.byPlayer, "byPlayer", true, false);
            Scribe_Values.Look(ref this.Props.enabled, "enabled", true, false);
            Scribe_Values.Look(ref this.Props.range, "range", 3, false);
        }
        public override void CompTick()
        {
            base.CompTick();
            if (this.Enabled && Find.TickManager.TicksGame % 250 == 0)
            {
                this.CompTickRare();
            }
        }
        public override void CompTickLong()
        {
            base.CompTickLong();
            this.CompTickRare();
        }
        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!this.Enabled)
            {
                //Log.Message($"[ZzZomboRW.CompClaimable] Not enabled at this time ({this.parent}).\nSpawned: {this.parent.Spawned};\nCapture by enemies: {this.Props.byEnemies};\nCapture by player: {this.Props.byPlayer};\nEnabled: {this.Props.enabled};\nRange: {this.Props.range}.");
                return;
            }
            Pawn pawn = null;
            var building = (Building)this.parent;
            var cellRect = building.OccupiedRect().ExpandedBy(this.Props.range).ClipInsideMap(building.Map);
            foreach (var c in cellRect)
            {
                foreach (var thing in c.GetThingList(building.Map))
                {
                    if (thing is Pawn _pawn && this.BuildingClaimableBy(building, _pawn, cellRect))
                    {
                        pawn = _pawn;
                        break;
                    }
                }
            }
            if (pawn != null)
            {
                if (building.Faction == Faction.OfPlayer)
                    Messages.Message("ZzZomboRW_ClaimOnTheGo_ClaimedByEnemy".Translate(building, pawn), this.parent,
                        MessageTypeDefOf.CautionInput, true);
                building.SetFaction(null, null);
                building.SetFaction(pawn.Faction, null);
                //Log.Warning($"[ZzZomboRW.CompClaimable] {building} captured by {pawn.Faction}, new faction: {building.Faction}.");
                SoundDefOf.Designate_Claim.PlayOneShot(new TargetInfo(this.parent.Position, this.parent.Map, false));
                foreach (IntVec3 cell in this.parent.OccupiedRect())
                {
                    MoteMaker.ThrowMetaPuffs(new TargetInfo(cell, this.parent.Map, false));
                }
            }
        }
        public bool BuildingClaimableBy(Building building, Pawn by, CellRect cellRect)
        {
            //Log.Warning($"[ZzZomboRW.CompClaimable] Check: pawn={by}, int.={by.RaceProps.intelligence}, building={building}, claimable = {building.def.Claimable}, `by.faction`={by.Faction}, tooluser={by.RaceProps.ToolUser}, is wild={by.IsWildMan()}.");
            //Log.Warning($"[ZzZomboRW.CompClaimable] Condition: factions={building.Faction == by.Faction || building.Faction == Faction.OfMechanoids}.");
            //Log.Warning($"[ZzZomboRW.CompClaimable] Condition: enemies={!building.Faction.HostileTo(by.Faction)}.");
            //Log.Warning($"[ZzZomboRW.CompClaimable] Condition: hives={HiveUtility.AnyHivePreventsClaiming(building)}.");
            if (by == null || building == null || !building.Spawned || !building.def.Claimable || by.Faction == null ||
                !by.RaceProps.ToolUser || by.IsWildMan())
            {
                return false;
            }
            if (building.Faction == by.Faction || building.Faction == Faction.OfMechanoids)
            {
                return false;
            }
            if (!building.Faction.HostileTo(by.Faction))
            {
                return false;
            }
            if (by.Faction != Faction.OfPlayer)
            {
                if (!this.Props.byEnemies)
                {
                    //Log.Warning("[ZzZomboRW.CompClaimable] Check: by enemies=false.");
                    return false;
                }
            }
            else
            {
                if (!this.Props.byPlayer || building.Fogged())
                {
                    //Log.Warning($"[ZzZomboRW.CompClaimable] Check: by player={this.Props.byPlayer}.");
                    //Log.Warning($"[ZzZomboRW.CompClaimable] Check: fogged={building.Fogged()}.");
                    return false;
                }
            }
            if (!GenHostility.IsActiveThreatTo(by, building.Faction))
            {
                //Log.Warning("[ZzZomboRW.CompClaimable] Check: not a threat.");
                return false;
            }
            foreach (var pawn in building.Map.mapPawns.SpawnedPawnsInFaction(by.Faction))
            {
                if (pawn.mindState?.enemyTarget == building)
                {
                    //Log.Warning("[ZzZomboRW.CompClaimable] Check: target.");
                    return false;
                }
            }
            if (HiveUtility.AnyHivePreventsClaiming(building))
            {
                return false;
            }
            var dest = new LocalTargetInfo(building);
            foreach (var c in cellRect)
            {
                foreach (var t in c.GetThingList(building.Map))
                {
                    if (t is Pawn pawn && pawn.RaceProps.ToolUser &&
                        GenHostility.IsActiveThreatTo(pawn, by.Faction) && GenSight.LineOfSightToThing(pawn.Position,
                            building, building.Map) && pawn.CanReach(dest, PathEndMode.Touch, Danger.Deadly, false,
                            TraverseMode.ByPawn))
                    {
                        //Log.Warning($"[ZzZomboRW.CompClaimable] Check: {pawn} prevents capture.");
                        return false;
                    }
                }
            }
            //Log.Warning($"[ZzZomboRW.CompClaimable] Condition: reachable={GenSight.LineOfSightToThing(by.Position, building, building.Map) && by.CanReach(dest, PathEndMode.Touch, Danger.Deadly, false, TraverseMode.ByPawn)}!");
            return GenSight.LineOfSightToThing(by.Position, building, building.Map) && by.CanReach(dest, PathEndMode.Touch, Danger.Deadly, false,
                TraverseMode.ByPawn);
        }
    }


}