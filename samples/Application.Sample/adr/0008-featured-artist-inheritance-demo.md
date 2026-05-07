# 0008 — Featured Artist entity: entity type inheritance demonstration

- **Status**: accepted
- **Date**: 2026-05-14
- **Author**: agent

## Context

ADR-0016 (root slice `Entity`) introduced entity type inheritance: a child entity
can extend a parent entity by declaring `[Entity(PredicatePath = "…")]` alongside
`public partial class Child : Parent`.  The generator emits the C# class hierarchy,
and `ReflectionRdfMapper` projects one `rdf:type` triple per ancestry level so that
`QueryByTypeAsync<Parent>()` returns instances of any subtype.

The existing sample entities (`Artist`, `Book`, `DataRecord`, `Genre`, `Recording`,
`Studio`) do not demonstrate this feature.  Without a concrete sample:

- The `[OperationEndpoints]` path on a child entity is undocumented.
- The polymorphic-listing behaviour (base-type GET returns subtypes) has no live proof.
- There is no reference pattern for child entities in the Bruno collection.

## Options

1. **Add `FeaturedArtist : Artist` entity** in `Entities/` carrying
   `[OperationEndpoints("featured-artists")]` and two extra predicates
   (`FeaturedSince: int`, `SponsorName: string?`).  Add Bruno chapter 14 with
   full CRUD plus a separate step that GETs `api/entities/artists` and asserts
   that the featured-artist IRI appears.
   Pro: builds directly on the existing `Artist` CRUD story (chapter 09);
   demonstrates the exact polymorphic-listing guarantee from ADR-0016.
2. **Extend an existing sub-domain** (e.g. add a `LeadArtist` under `Artist` in
   the Studio entity story).  Con: complicates the Studio/Recording/Genre chapter
   with an unrelated concern.
3. **No sample entity** — leave demonstration to unit tests only.
   Con: loses the live runnable proof that the HTTP stack handles subtypes end-to-end.

## Decision

Option 1.

- `samples/Application.Sample/Entities/FeaturedArtist.cs`:
  ```csharp
  [Entity(PredicatePath = "featured-artist")]
  [OperationEndpoints("featured-artists")]
  public partial class FeaturedArtist : Artist { … }
  ```
  `Path` is omitted from `[Entity]` per FORGE0007 (child entities must not redeclare
  `Path`).  The explicit `[OperationEndpoints("featured-artists")]` parameter supplies
  the route segment, taking priority over the (absent) `[Entity(Path)]`.

- `AddOperationEndpointsHttp` is updated to resolve `[Identity]` with `inherit: true`
  so that child entities — which must not redeclare `[Identity]` per FORGE0006 — still
  pass the validation guard.

- Bruno chapter 14 (`14-featured-artists/`) contains six requests in sequence:
  | # | File | Purpose |
  |---|------|---------|
  | 1 | `01-create.bru` | POST a `FeaturedArtist`; capture IRI in `featuredArtistIri` |
  | 2 | `02-list.bru` | GET `featured-artists` list; assert `items` defined |
  | 3 | `03-read.bru` | GET by IRI; assert `featuredSince` and `sponsorName` |
  | 4 | `04-update.bru` | PUT with new `featuredSince` and `sponsorName` |
  | 5 | `05-list-artists-includes-featured.bru` | GET `artists`; assert `featuredArtistIri` appears (**polymorphic listing proof**) |
  | 6 | `06-delete.bru` | DELETE; clean up |

## Consequences

- `GET api/entities/artists` returns all `Artist` instances together with all registered
  `FeaturedArtist` instances because both share the `<…/artists>` `rdf:type` triple.
- `GET api/entities/featured-artists` returns only `FeaturedArtist` instances (filtered
  on `<…/artists/FeaturedArtist>` type IRI).
- Child entities with `[OperationEndpoints]` MUST supply the route segment through
  `[OperationEndpoints("path")]` (not `[Entity(Path)]`) — this is the established
  pattern going forward.
- The `inherit: true` guard fix in `AddOperationEndpointsHttp` is backward-compatible;
  existing entities that declare `[Identity]` directly are unaffected.
