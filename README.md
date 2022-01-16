This program is currently a work in progress  
  
Some games use TCP for their network transfer. There is a downside where a lost packet will "hold" back the rest of the packets after it, and it will take generally 1.2 round trip times to fix this.  
  
This program gets around this by retransmitting packets BEFORE the round trip delay, basically __trades bandwidth for latency__. In cases where it matters (~150ms+) this means you will use __3-4x the network traffic__. The good news is minecraft generally only uses 5-10kb/s, so this isn't really a concern, and is the game I had in mind when creating this.  
  
TLDR: DarkTunnel makes the game use more internet so you can play minecraft with your friends across the world.  
