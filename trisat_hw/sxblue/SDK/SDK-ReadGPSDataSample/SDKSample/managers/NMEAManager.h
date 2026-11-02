//
//  NMEAManager.h
// SDK Sample
//
//  Created by Sharad Garg on 11/12/14.
//  Copyright (c) 2014 Aman Enterprises Inc. All rights reserved.

// IMPORTANT PERFORMANCE RECOMMENDATION:
// IT IS BETTER TO NOT USE NOTIFICATION CENTER AND CALL ON TARGET's PERFORMSELECTOR DELEGATE METHOD DIRECTLY BUT TO SHOW THE CONCEPT
// WE HAVE INCLUDED THE NOTIFICATION CENTER CALL INSTEAD. IF YOU HAVE ALOT OF INDIVIDUAL DELEGATES TO CALL THEN NOTIFICATION CENTER
// WOULD MAKE SENSE... OTHERWISE CALLING CALLING PERFROMSELECTOR IS MORE EFFICIENT.
//

#import <Foundation/Foundation.h>
#include "nmea.h"

// notification is posted on each new nmea sentence on background thread
UIKIT_EXTERN NSString * const kNMEASentenceNotification;

@interface NMEAManager : NSObject

+ (NMEAManager *)sharedManager;

- (void)initParser;
- (NSUInteger)parse:(NSData *)data;     // returns the number of NMEA sentences parsed

- (nmeaINFO)getNmeaInfo;        // returns the current NMEA info parsed

@end
