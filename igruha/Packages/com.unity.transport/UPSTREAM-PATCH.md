# Unity Transport 2.6.0 — IGR-631

This is an embedded copy of the resolved Unity Transport 2.6.0 package, with
one runtime change in `Runtime/UDPNetworkInterface.cs`: release the receive
buffer when Baselib reports a failed receive completion.

On Windows, packets sent to a forcibly terminated client can produce ICMP
port-unreachable responses. The original failed-completion branch retains
each buffer. Once the receive pool is exhausted, the host stops receiving
from every surviving client. Increasing the queue only delays the failure.

Unity confirmed this bug and this exact fix on 2026-09-09:
https://discussions.unity.com/t/unity-transport-6-6-0-udp-host-stops-receiving-after-abrupt-client-disconnect-on-windows/1736023

The same branch exists in this project's 2.6.0 package. Embedding retains the
existing version and protocol; no firewall setting is required. Both peers
must receive a new build to pick up the fix in their local transport.

Remove the embedded package after installing and verifying an official
release containing this fix. Preserve the upstream LICENSE and notices.
