I'm considering a major revamp of the Agent Pipeline @apps\agent\backend/ as well as affected @apps\orchestrator\web/ and @apps\orchestrator\backend/ code. A more simplified but clearer version of orchestrator-initiated installation of packages (defined in the workload definition JSON) using libraries if there are or something that works. 

Note that currently these features work:
- can enroll a remote agent node (tested on a VM agent)
- can ingest artifacts (artifacts = installer media binary & its manifest JSON)
- can upload workload JSON definitions, referencing packages in the local artifact store (in orchestrator, see @test-paths.md )

What do you think? Is this a smart decision? Let's decide if this is doable and better to do given the state of the codebase right now, before committing to this major revamp. You may review the codebase, @README.md , etc. (don't rely on docs because they might be outdated, always check reality in the codebase).
