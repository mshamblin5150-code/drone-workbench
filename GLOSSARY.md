# Drone Workbench

Drone Workbench gives a human a view of agent work and its effects on a computer,
while keeping observation, ownership and permission to act distinct.

## Language

**Drone**:
An automated agent participating in work. A drone is not necessarily a separate
operating-system process.
_Avoid_: process, PID as synonyms for drone

**Source**:
A particular reporting instance that supplies observations or supported controls.
Its reported scope and freshness qualify what Workbench can claim.
_Avoid_: assuming one source represents every instance of an application

**Observation**:
A dated claim from a source about activity, resources or an artifact. An
observation is evidence rather than permanent truth about an external object.

**Collector**:
Workbench's continuously running monitoring that gathers observations while the
user is signed in.
_Avoid_: agent, drone, daemon

**Monitoring stop**:
A deliberate user instruction pausing the collector. It stays in force across
restarts and sign-ins until the user resumes monitoring.
_Avoid_: bare "stop", which belongs to job control

**Retry exhaustion**:
The collector failed repeatedly after unexpected exits and gave up until the next
sign-in or a user resume. It is distinct from a monitoring stop.

**Data age**:
How long ago a view's information was last confirmed by an observation.
_Avoid_: "live" as a promise of freshness

**Coverage**:
The declared scope a source can report, including unavailable, unsupported or
unverified parts. Missing coverage does not establish that no work exists.

**Job**:
A tracked piece of work with its own identity and outcomes. Its relationship to
conversations, delegated agents and processes is separately evidenced.

**Ownership**:
Evidence establishing responsibility for a specific workload or artifact.
Association, visibility and a matching name do not establish ownership.

**Control route**:
A supported way to request an action against a verified workload identity.
Acceptance of a request is distinct from its confirmed outcome.

**Artifact**:
A file, working directory, worktree, deliverable or other retained product of work.
Its preservation requirements are distinct from its producer's current state.

**Cleanup proposal**:
The exact targets, actions, preservation requirements and recovery terms presented
for review. A proposal is neither permission to execute nor proof of removability.

**Cleanup run**:
One approved execution of a specific freshly checked proposal, with per-target
progress and outcomes. Changed material facts require fresh review.

**Recovery copy**:
Preserved material and reconstruction information retained so removed work can
be restored. It has its own holding and review rules.

**Ordinary history**:
Routine filtered observations and work summaries retained for dashboard search.
It is distinct from required cleanup evidence and recovery material.
