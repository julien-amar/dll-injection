//! Minimal native target process.
//! Loops for a few seconds so a payload can be injected into it, then exits.

use std::{thread, time::Duration};

fn main() {
    let pid = std::process::id();
    println!("[native-target] started, PID = {pid}");

    for i in 0..20 {
        println!("[native-target] tick {i}");
        thread::sleep(Duration::from_millis(500));
    }

    println!("[native-target] done");
}
