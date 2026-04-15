MUST need to create a storyboard (flow):

fresh installation (orchestrator host & remote/agent hosts)

modify

1 how media is packaged (ISO, Exe)
2 Fresh Install (main node)
3 Sub node installation (remote install)
4 How to install updates
5 Modify workload
    - since need to also update orchestrator host from time to time,
        - must need to 
    - modify an update or installation from orchestrator (as sysadmin) to remote machine
        - change versions (ex. node 22 -> 24 or downgrade node version)

---

each step (ex. rest api -> sqlite) should be verified
- security on top of SignalR+mTLS:
    - how child process is secured/comes from agent itself, etc.?

use sqlite instead of sql server
- this PoC simpler
- does sqlite have a plugin for storing logs or what? (for otel)

all will be bootstrapped by this project

how pkgs will be pulled
- NO EXTERNAL SOURCE (Artifacts source, database)
- for agents, how will they install packages since there is no external package source
    - so im assuming agents might need to hit an endpoint from orchestrator for packages maybe?

SignalR
- can SignalR handle large chunks?
- how to integrate mTLS certs?
    - mTLS: need explanation docs on how this is used to secure stuff


if fresh Orchestrator:
- how to download packages in the first place (since no external source)
    - drag&drop to web UI or via an endpoint or what?
    - zip files? binaries/executables?

packages (pkgs)
- no need specific C++/legacy compatibility issue stuff -> ALL packages can have a C# wrapper or can be executable (so no need language-specific deps, etc.) 
- need security 
    - ex. how to make sure verified orchestrator,
    - signed packages before running
        - how is it signed in the first place? where stored
- versioned packages (canary, test, SHA-based, etc.)
- packages to be deployed: these can range from applications (ex. sql server, etc.) to specific binaries/executables or software components
    - must address brittleness, or if the installation can be retried

Job Pipeline
- have a way to distinct packages that the agent can run in serial(? is this "in sequence"), or parallel
- indicator of what job/step is currently running
- **THE COMPLEXITY WE MUST FOCUS ON** (in this project) are the **granularity and complexity of the packages**
    - there are some jobs that are long, multi-step (can they be retried or not? idempotent?)
    - some jobs or STEPS are brittle - how will this be handled?
        - self-healing (auto retry)? but how to determine that a job or step is "retry-able"?

usecase: for customers windows workstations/desktops

otel
- security - hide logs, etc.
- where store logs? - should only spin up logs store if absolutely necessary 
    - does sqlite have a plugin for storing logs or what?
        - how about postgresql?

rollback/self-healing
- simple mechanism only for now on simple jobs
- if job can be retried then auto retry it
    - how to "tag" these jobs as simple and "can be retried"
    - no specific implementation mechanism plan here - maybe use traces to see specific step at which job failed?
- must be idempotent - BUT since other packages (ex. sql server) cannot be idempotent (or can it be?)
    - then must also have a way to "tag" these jobs or packages as "not idempotent" or "risky" if that makes sense 

security in between
- security on top of SignalR+mTLS

distinction between: Ping (orch -> agent) vs LeaseHeartbeat (agent -> orch)

can showcase simple Windows to Linux (agent, remote machine) installation
- not a priority though

"how agents are installed/bootstrapped?"
- in PoC, it is accepted to just run a manual script on the remote machine
- no need for GPO/SCCM - for docs purposes, note this GPO/SCCM approach as "considered"

---
what are the trust boundary annotations in Architecture diagram?

keep in mind that that this won't need SCALE as much as to need multiple orchestrators. 


---
upload on teams
