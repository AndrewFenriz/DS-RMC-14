﻿using System.Collections.Immutable;
using System.Linq;
using Content.Shared._RMC14.Admin;
using Content.Shared._RMC14.Chat;
using Content.Shared._RMC14.Marines.Announce;
using Content.Shared._RMC14.Marines.Orders;
using Content.Shared._RMC14.Pointing;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Chat;
using Content.Shared.Clothing;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Prototypes;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Radio.EntitySystems;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Marines.Squads;

public sealed class SquadSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly EncryptionKeySystem _encryptionKey = default!;
    [Dependency] private readonly SharedIdCardSystem _id = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedJobSystem _job = default!;
    [Dependency] private readonly SharedMarineSystem _marine = default!;
    [Dependency] private readonly SharedMarineAnnounceSystem _marineAnnounce = default!;
    [Dependency] private readonly SharedMarineOrdersSystem _marineOrders = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedRMCBanSystem _rmcBan = default!;
    [Dependency] private readonly SharedCMChatSystem _rmcChat = default!;

    private static readonly ProtoId<JobPrototype> SquadLeaderJob = "CMSquadLeader";

    public ImmutableArray<EntityPrototype> SquadPrototypes { get; private set; }
    public ImmutableArray<JobPrototype> SquadRolePrototypes { get; private set; }

    private readonly HashSet<EntityUid> _membersToUpdate = new();

    private EntityQuery<SquadArmorWearerComponent> _squadArmorWearerQuery;
    private EntityQuery<SquadMemberComponent> _squadMemberQuery;
    private EntityQuery<SquadTeamComponent> _squadTeamQuery;

    public override void Initialize()
    {
        _squadArmorWearerQuery = GetEntityQuery<SquadArmorWearerComponent>();
        _squadMemberQuery = GetEntityQuery<SquadMemberComponent>();
        _squadTeamQuery = GetEntityQuery<SquadTeamComponent>();

        SubscribeLocalEvent<SquadArmorComponent, GetEquipmentVisualsEvent>(OnSquadArmorGetVisuals, after: [typeof(ClothingSystem)]);

        SubscribeLocalEvent<SquadMemberComponent, MapInitEvent>(OnSquadMemberMapInit);
        SubscribeLocalEvent<SquadMemberComponent, ComponentRemove>(OnSquadMemberRemove);
        SubscribeLocalEvent<SquadMemberComponent, EntityTerminatingEvent>(OnSquadMemberTerminating);
        SubscribeLocalEvent<SquadMemberComponent, MobStateChangedEvent>(OnSquadMemberMobStateChanged);
        SubscribeLocalEvent<SquadMemberComponent, PlayerAttachedEvent>(OnSquadMemberPlayerAttached);
        SubscribeLocalEvent<SquadMemberComponent, PlayerDetachedEvent>(OnSquadMemberPlayerDetached);
        SubscribeLocalEvent<SquadMemberComponent, GetMarineIconEvent>(OnSquadRoleGetIcon, after: [typeof(SharedMarineSystem)]);

        SubscribeLocalEvent<SquadLeaderComponent, EntityTerminatingEvent>(OnSquadLeaderTerminating);
        SubscribeLocalEvent<SquadLeaderComponent, GetMarineIconEvent>(OnSquadLeaderGetMarineIcon, after: [typeof(SharedMarineSystem)]);

        SubscribeLocalEvent<SquadLeaderHeadsetComponent, EncryptionChannelsChangedEvent>(OnSquadLeaderHeadsetChannelsChanged);
        SubscribeLocalEvent<SquadLeaderHeadsetComponent, EntityTerminatingEvent>(OnSquadLeaderHeadsetTerminating);

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        RefreshSquadPrototypes();
    }

    private void OnSquadArmorGetVisuals(Entity<SquadArmorComponent> ent, ref GetEquipmentVisualsEvent args)
    {
        if (_inventory.TryGetSlot(args.Equipee, args.Slot, out var slot) &&
            (slot.SlotFlags & ent.Comp.Slot) == 0)
        {
            return;
        }

        if (!_squadMemberQuery.TryComp(args.Equipee, out var member) ||
            !_squadArmorWearerQuery.TryComp(args.Equipee, out var wearer))
        {
            return;
        }

        var rsi = wearer.Leader ? ent.Comp.LeaderRsi : ent.Comp.Rsi;
        var layer = $"enum.{nameof(SquadArmorLayers)}.{ent.Comp.Layer}";
        if (args.Layers.Any(l => l.Item1 == layer))
            return;

        args.Layers.Add((layer, new PrototypeLayerData
        {
            RsiPath = rsi.RsiPath.ToString(),
            State = rsi.RsiState,
            Color = member.BackgroundColor,
            Visible = true,
        }));
    }

    private void OnSquadMemberMapInit(Entity<SquadMemberComponent> ent, ref MapInitEvent args)
    {
        _membersToUpdate.Add(ent);
    }

    private void OnSquadMemberRemove(Entity<SquadMemberComponent> ent, ref ComponentRemove args)
    {
        if (_squadTeamQuery.TryComp(ent.Comp.Squad, out var team))
            team.Members.Remove(ent);
    }

    private void OnSquadMemberTerminating(Entity<SquadMemberComponent> ent, ref EntityTerminatingEvent args)
    {
        if (_squadTeamQuery.TryComp(ent.Comp.Squad, out var team))
            team.Members.Remove(ent);
    }

    private void OnSquadMemberMobStateChanged(Entity<SquadMemberComponent> ent, ref MobStateChangedEvent args)
    {
        if (ent.Comp.Squad is not { } squad)
            return;

        var ev = new SquadMemberUpdatedEvent(squad);
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnSquadMemberPlayerAttached(Entity<SquadMemberComponent> ent, ref PlayerAttachedEvent args)
    {
        if (ent.Comp.Squad is not { } squad)
            return;

        var ev = new SquadMemberUpdatedEvent(squad);
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnSquadMemberPlayerDetached(Entity<SquadMemberComponent> ent, ref PlayerDetachedEvent args)
    {
        if (ent.Comp.Squad is not { } squad)
            return;

        var ev = new SquadMemberUpdatedEvent(squad);
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnSquadRoleGetIcon(Entity<SquadMemberComponent> member, ref GetMarineIconEvent args)
    {
        args.Background = member.Comp.Background;
        args.BackgroundColor = member.Comp.BackgroundColor;
    }

    private void OnSquadLeaderTerminating(Entity<SquadLeaderComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Headset is { } headset)
            RemCompDeferred<SquadLeaderHeadsetComponent>(headset);
    }

    private void OnSquadLeaderGetMarineIcon(Entity<SquadLeaderComponent> ent, ref GetMarineIconEvent args)
    {
        args.Icon = ent.Comp.Icon;
    }

    private void OnSquadLeaderHeadsetChannelsChanged(Entity<SquadLeaderHeadsetComponent> ent, ref EncryptionChannelsChangedEvent args)
    {
        foreach (var channel in ent.Comp.Channels)
        {
            args.Component.Channels.Add(channel);
        }
    }

    private void OnSquadLeaderHeadsetTerminating(Entity<SquadLeaderHeadsetComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryComp(ent.Comp.Leader, out SquadLeaderComponent? leader))
        {
            leader.Headset = null;
            Dirty(ent.Comp.Leader, leader);
        }
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        if (ev.WasModified<EntityPrototype>() || ev.WasModified<JobPrototype>())
            RefreshSquadPrototypes();
    }

    private void RefreshSquadPrototypes()
    {
        var entBuilder = ImmutableArray.CreateBuilder<EntityPrototype>();
        foreach (var entity in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (entity.HasComponent<SquadTeamComponent>())
                entBuilder.Add(entity);
        }

        entBuilder.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        SquadPrototypes = entBuilder.ToImmutable();

        var jobBuilder = ImmutableArray.CreateBuilder<JobPrototype>();
        foreach (var job in _prototypes.EnumeratePrototypes<JobPrototype>())
        {
            if (job.HasSquad)
                jobBuilder.Add(job);
        }

        SquadRolePrototypes = jobBuilder.ToImmutable();
    }

    public bool TryGetSquad(EntProtoId prototype, out Entity<SquadTeamComponent> squad)
    {
        var squadQuery = EntityQueryEnumerator<SquadTeamComponent, MetaDataComponent>();
        while (squadQuery.MoveNext(out var uid, out var team, out var metaData))
        {
            if (metaData.EntityPrototype?.ID != prototype.Id)
                continue;

            squad = (uid, team);
            return true;
        }

        squad = default;
        return false;
    }

    public bool TryGetMemberSquad(Entity<SquadMemberComponent?> member, out Entity<SquadTeamComponent> squad)
    {
        squad = default;
        if (!Resolve(member, ref member.Comp, false))
            return false;

        if (!TryComp(member.Comp.Squad, out SquadTeamComponent? team))
            return false;

        squad = (member.Comp.Squad.Value, team);
        return true;
    }

    public bool TryEnsureSquad(EntProtoId id, out Entity<SquadTeamComponent> squad)
    {
        if (!_prototypes.TryIndex(id, out var prototype) ||
            !prototype.HasComponent<SquadTeamComponent>(_compFactory))
        {
            squad = default;
            return false;
        }

        if (TryGetSquad(id, out squad))
            return true;

        var squadEnt = Spawn(id);
        if (!TryComp(squadEnt, out SquadTeamComponent? squadComp))
        {
            Log.Error($"Squad entity prototype {id} had {nameof(SquadTeamComponent)}, but none found on entity {ToPrettyString(squadEnt)}");
            return false;
        }

        squad = (squadEnt, squadComp);
        return true;
    }

    public int GetSquadMembersAlive(Entity<SquadTeamComponent> team)
    {
        var count = 0;
        var members = EntityQueryEnumerator<SquadMemberComponent>();
        while (members.MoveNext(out var uid, out var member))
        {
            if (member.Squad == team && !_mobState.IsDead(uid))
                count++;
        }

        return count;
    }

    public void AssignSquad(EntityUid marine, Entity<SquadTeamComponent?> team, ProtoId<JobPrototype>? job)
    {
        if (!Resolve(team, ref team.Comp))
            return;

        var member = EnsureComp<SquadMemberComponent>(marine);
        if (_squadTeamQuery.TryComp(member.Squad, out var oldSquad))
        {
            oldSquad.Members.Remove(marine);

            if (_mind.TryGetMind(marine, out var mindId, out _) &&
                _job.MindTryGetJobId(mindId, out var currentJob) &&
                currentJob != null)
            {
                if (oldSquad.Roles.TryGetValue(currentJob.Value, out var oldJobs) &&
                    oldJobs > 0)
                {
                    oldSquad.Roles[currentJob.Value] = oldJobs - 1;
                }
            }
        }

        member.Squad = team;
        member.Background = team.Comp.Background;
        member.BackgroundColor = team.Comp.Color;
        Dirty(marine, member);

        var grant = EnsureComp<SquadGrantAccessComponent>(marine);
        grant.AccessLevels = team.Comp.AccessLevels;

        if (_prototypes.TryIndex(job, out var jobProto))
        {
            grant.RoleName = $"{Name(team)} {jobProto.LocalizedName}";
        }
        else if (_mind.TryGetMind(marine, out var mindId, out _) &&
                 _job.MindTryGetJobName(mindId, out var name))
        {
            MarineSetTitle(marine, $"{Name(team)} {name}");
        }

        Dirty(marine, grant);

        team.Comp.Members.Add(marine);
        if (job != null)
        {
            team.Comp.Roles.TryGetValue(job.Value, out var roles);
            team.Comp.Roles[job.Value] = roles + 1;
        }

        var ev = new SquadMemberUpdatedEvent(team);
        RaiseLocalEvent(marine, ref ev);
    }

    private void MarineSetTitle(EntityUid marine, string title)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities(marine))
        {
            if (TryComp(item, out IdCardComponent? idCard))
                _id.TryChangeJobTitle(item, title, idCard);
        }
    }

    public void RefreshSquad(Entity<SquadTeamComponent?> squad)
    {
        if (!_squadTeamQuery.Resolve(squad, ref squad.Comp, false))
            return;

        var toRemove = new List<EntityUid>();
        foreach (var member in squad.Comp.Members)
        {
            if (TerminatingOrDeleted(member) ||
                !_squadMemberQuery.TryComp(member, out var memberComp) ||
                memberComp.Squad != squad)
            {
                toRemove.Add(member);
            }
        }

        squad.Comp.Members.ExceptWith(toRemove);
        Dirty(squad);

        foreach (var member in toRemove)
        {
            var ev = new SquadMemberUpdatedEvent(squad);
            RaiseLocalEvent(member, ref ev);
        }
    }

    public bool IsInSquad(Entity<SquadMemberComponent?> member, EntProtoId<SquadTeamComponent> squad)
    {
        if (!Resolve(member, ref member.Comp, false))
            return false;

        return member.Comp.Squad is { } memberSquad && Prototype(memberSquad)?.ID == squad.Id;
    }

    public void PromoteSquadLeader(Entity<SquadMemberComponent?> toPromote, EntityUid user)
    {
        if (HasComp<SquadLeaderComponent>(toPromote))
            return;

        if (_rmcBan.IsJobBanned(toPromote.Owner, SquadLeaderJob))
        {
            _popup.PopupCursor($"{Name(toPromote)} is unfit to lead!", user, PopupType.MediumCaution);
            return;
        }

        if (_mobState.IsDead(toPromote))
        {
            _popup.PopupCursor($"{Name(toPromote)} is KIA!", user, PopupType.MediumCaution);
            return;
        }

        if (Resolve(toPromote, ref toPromote.Comp, false))
        {
            var leaders = EntityQueryEnumerator<SquadLeaderComponent, SquadMemberComponent>();
            while (leaders.MoveNext(out var uid, out var leader, out var otherMember))
            {
                if (leader.Headset is { } headset)
                {
                    RemComp<SquadLeaderHeadsetComponent>(headset);
                    if (TryComp(headset, out EncryptionKeyHolderComponent? holder))
                        _encryptionKey.UpdateChannels(headset, holder);
                }

                if (otherMember.Squad == toPromote.Comp.Squad)
                {
                    if (TryComp(uid, out MarineComponent? otherMarine) &&
                        Equals(otherMarine.Icon, leader.Icon))
                    {
                        _marine.ClearIcon((uid, otherMarine));
                    }

                    if (TryComp(uid, out MarineOrdersComponent? otherOrders) &&
                        !otherOrders.Intrinsic)
                    {
                        RemCompDeferred<MarineOrdersComponent>(uid);
                    }

                    RemComp<SquadLeaderComponent>(uid);
                    RemCompDeferred<RMCPointingComponent>(uid);
                }
            }
        }

        var newLeader = EnsureComp<SquadLeaderComponent>(toPromote);
        if (!EnsureComp(toPromote, out MarineOrdersComponent orders))
        {
            orders.Intrinsic = false;
            Dirty(toPromote, orders);
            _marineOrders.StartActionUseDelay((toPromote, orders));
        }

        EnsureComp<RMCPointingComponent>(toPromote);

        var slots = _inventory.GetSlotEnumerator(toPromote.Owner, SlotFlags.EARS);
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } contained)
                continue;

            if (TryComp(contained, out EncryptionKeyHolderComponent? holder))
            {
                newLeader.Headset = contained;
                Dirty(toPromote, newLeader);
                EnsureComp<SquadLeaderHeadsetComponent>(contained);
                _encryptionKey.UpdateChannels(contained, holder);
                break;
            }
        }

        var squad = toPromote.Comp?.Squad;
        if (TryComp(toPromote, out ActorComponent? actor))
        {
            var squadStr = Exists(squad) ? $" for {Name(squad.Value)}" : string.Empty;
            var message = $"Overwatch: You've been promoted to 'ACTING SQUAD LEADER'{squadStr}. Your headset has access to the command channel (:v).";
            _rmcChat.ChatMessageToOne(ChatChannel.Local, message, message, default, false, actor.PlayerSession.Channel, Color.FromHex("#0084FF"), true);
        }

        if (Exists(squad) && Prototype(squad.Value) is { } squadProto)
        {
            _marineAnnounce.AnnounceSquad($"Attention: A new Squad Leader has been set: {Name(toPromote)}", squadProto.ID);
            _popup.PopupCursor($"{Name(toPromote)} is {Name(squad.Value)}'s new leader!", user, PopupType.Medium);
        }
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<SquadGrantAccessComponent>();
        while (query.MoveNext(out var uid, out var grant))
        {
            if (grant.RoleName != null)
                MarineSetTitle(uid, grant.RoleName);

            foreach (var item in _inventory.GetHandOrInventoryEntities(uid))
            {
                if (grant.AccessLevels.Length > 0 &&
                    TryComp(item, out AccessComponent? access))
                {
                    foreach (var level in grant.AccessLevels)
                    {
                        access.Tags.Add(level);
                    }

                    Dirty(item, access);
                }

                if (HasComp<IdCardComponent>(item) &&
                    !EnsureComp<IdCardOwnerComponent>(item, out var owner))
                {
                    owner.Id = uid;
                }
            }

            RemCompDeferred<SquadGrantAccessComponent>(uid);
        }

        foreach (var toUpdate in _membersToUpdate)
        {
            if (TerminatingOrDeleted(toUpdate))
                continue;

            if (!_squadMemberQuery.TryComp(toUpdate, out var member) ||
                !_squadTeamQuery.TryComp(member.Squad, out var squad))
            {
                continue;
            }

            squad.Members.Add(toUpdate);
        }

        _membersToUpdate.Clear();
    }
}
