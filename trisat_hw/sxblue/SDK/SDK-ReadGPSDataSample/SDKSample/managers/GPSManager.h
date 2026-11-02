//
//  GPSManager.h
// SDK Sample
//
//  Created by Sharad Garg on 11/12/14.
//  Copyright (c) 2014 Aman Enterprises Inc. All rights reserved.
//

#import <Foundation/Foundation.h>
#import <CoreLocation/CoreLocation.h>

@interface GPSManager : NSObject

+ (CLLocationManager *)sharedLocationManager;

@end
