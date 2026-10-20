//
//  ConnectionManagerRead.h
//  Location Cable
//
//  Created by Sharad Garg on 11/12/14.
//  Copyright (c) 2014 Aman Enterprises Inc. All rights reserved.
//

#import <Foundation/Foundation.h>

#include "serialComDefs.h"

//////////////////////////
//
// Connection manager delegate protocol
// Register an object as connection manager delegate and receive messages according to
// this protocol on a background thread (non blocking, when reading lot of data for example)
//
//////////////////////////
@protocol ConnectionManagerDelegate <NSObject>

@optional
// GPS Cable has been connected and/or application moved to foreground.
// protocol is the string which matched from the protocol list passed to initWithProtocol:
- (void)connectionConnected:(NSString *)protocol;

// GPS Cable was disconnected and/or application moved to background
- (void)connectionDisconnected;

// serial port status has changed
// user can call getModemStatus or getPortStatus to get current state
- (void)portStatusChanged;

// bytes are available to be read (user should call read:, getDataFromBytesAvailable, or getStringFromBytesAvailable)
- (void)readBytesAvailable:(UInt32)length;

// called when a response is received to a getPortConfig call
- (void)didReceivePortConfig;

// GPS Cable called with result when loop test completes.
- (void)didGpsLoopTest:(BOOL)pass;

// Rx / Tx counters changed
- (void)updateStats;

@end

/////////////////////////
//
// Cable manager API
// cable manager manages its own thread for I/O
//
/////////////////////////
@interface ConnectionManager : NSObject

+ (ConnectionManager *)sharedConnectionManager;     // get the unique instance

- (void)setDelegate:(id<ConnectionManagerDelegate>)delegate;     // set a delegate

- (BOOL)isConnectionConnected;       // Ask if cable is physically plugged in
- (NSUInteger)getRxCounter;     // Ask for # bytes received since last pluggin or reset
//- (NSUInteger)getTxCounter;     // Ask for # bytes sent since last pluggin or reset
- (void)resetCounters;          // set tx and rx bytes to 0
- (NSThread *)getConnectionManagerThread;    // Ask for the cable manager thread handling I/O

// simple serial port config interface
// can be called anytime
- (void)setBaud:(int)baud;
- (void)setDataSize:(DataSizeType)dataSize;
- (void)setParity:(ParityType)parity;
- (void)setStopBits:(StopBitsType)stopBits;

- (int)getBaud;

// returns TRUE if connected cable supports extended baud rates ( > 57600)
- (BOOL)supportsExtendedBaudRates;

// read serial bytes
- (int)read:(UInt8 *)data length:(UInt32)length;
- (int)getReadBytesAvailable;

// returns an NSString containing the available bytes in rx fifo as a string.
// assumes the data is ASCII and encoded as UTF8
// calling this clears rx fifo similar to read:
- (NSString *)getStringFromBytesAvailable;

// returns an NSData containing the available bytes in rx fifo
// calling this clears rx fifo similar to read:
- (NSData *)getDataFromBytesAvailable;

/*
 returns a bit field (see serialComDefs.h)
 0-3 current modem status bits for CTS, RI, DSR, DCD, 4-7 previous modem status bits
 
 MODEM_STAT_CTS  0x01
 MODEM_STAT_RI   0x02
 MODEM_STAT_DSR  0x04
 MODEM_STAT_DCD  0x08*/
 
- (int)getModemStatus;

// returns true if DTR is asserted
- (BOOL)getDtr;

// returns true if RTS is asserted
- (BOOL)getRts;

// set DTR state
- (void)setDtr:(BOOL)enable;

// set RTS state
- (void)setRts:(BOOL)enable;

// advanced (full) serial port config interface (see serialComDefs.h)
- (void)setPortConfig:(serialPortConfig *)config requestStatus:(BOOL)reqStatus;
- (void)setPortControl:(serialPortControl *)control requestStatus:(BOOL)reqStatus;
- (void)getPortConfig:(serialPortConfig *) portConfig;
- (void)getPortStatus:(serialPortStatus *) portStatus;

// GPS cable only - requires loopback connector
- (void)testGpsCable;

@end
