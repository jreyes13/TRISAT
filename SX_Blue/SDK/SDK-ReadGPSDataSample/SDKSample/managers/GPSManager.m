//
//  GPSManager.m
// SDK Sample
//
//  Created by Sharad Garg on 11/12/14.
//  Copyright (c) 2014 Aman Enterprises Inc. All rights reserved.
//

#import "GPSManager.h"

static CLLocationManager *_locationManager;
static dispatch_once_t locationManagerToken;

@implementation GPSManager

+ (CLLocationManager *)sharedLocationManager
{
	dispatch_once(&locationManagerToken, ^{
        _locationManager = [[CLLocationManager alloc] init];
        _locationManager.desiredAccuracy = kCLLocationAccuracyBest;
    });
	
	return _locationManager;
}

@end
