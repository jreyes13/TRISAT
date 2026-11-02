//
//  NMEAManager.m
// SDK Sample
//
//  Created by Sharad Garg on 11/12/14.
//  Copyright (c) 2014 Aman Enterprises Inc. All rights reserved.
//

#import "NMEAManager.h"

#include "nmea.h"
#include "DLog.h"

// libnmea callbacks
void nmeaTrace(const char *str, int str_size)
{
    // NMEA sentence parsed correctly
    NSString *traceStr = [[NSString alloc] initWithBytes:str length:str_size encoding:NSUTF8StringEncoding];
    DLog(@"%@",traceStr);
    NSNotification *aNotif = [NSNotification notificationWithName:kNMEASentenceNotification object:traceStr];
    [[NSNotificationCenter defaultCenter] postNotification:aNotif];
}
void nmeaError(const char *str, int str_size)
{
    NSString *errorStr = [[NSString alloc] initWithBytes:str length:str_size encoding:NSUTF8StringEncoding];
    DLog(@"%@", errorStr);
}

static NMEAManager * _sharedManager;
static dispatch_once_t nmeaManagerToken;

NSString * const kNMEASentenceNotification = @"kNMEASentenceNotification";

@interface NMEAManager () {
    nmeaINFO info;
    nmeaPARSER parser;
}

@end


@implementation NMEAManager

+ (NMEAManager *)sharedManager {
    dispatch_once(&nmeaManagerToken, ^{
        _sharedManager = [[NMEAManager alloc] init];
        [_sharedManager initParser];
    });
	return _sharedManager;
}

- (void)initParser {
    DLog(@"enter");
    nmea_zero_INFO(&info);      // reset info for results
    
    nmea_property()->trace_func = &nmeaTrace;
    nmea_property()->error_func = &nmeaError;
    
    if (parser.buffer!=NULL) {
        nmea_parser_destroy(&parser);   // destroy previously created parser
    }
    nmea_parser_init(&parser);          // init parser
}

- (NSUInteger)parse:(NSData *)data {
    DLog(@"enter");
    // let's get a C buffer from data
    char *buff = (char *)[data bytes];
    NSUInteger res = nmea_parse(&parser, buff, [data length], &info);    // updates info
    
    // small resumé
    DLog(@"analysed %i sentences", res);
    DLog(@"mask is %i", info.smask);
    DLog(@"sig is %i, fix is %i", info.sig, info.fix);
    DLog(@"GPS satellites in view %i, in use %i", info.GPSsatinfo.inview, info.GPSsatinfo.inuse);
    DLog(@"GLONASS satellites in view %i, in use %i", info.GLONASSsatinfo.inview, info.GLONASSsatinfo.inuse);
    
    return res;
}

- (nmeaINFO)getNmeaInfo {
    DLog(@"enter");
    return info;
}

@end
