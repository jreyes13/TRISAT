//
//  SatellitesView.h
//  SatellitesView
//
//  Created by Sharad Garg on 11/12/14.
//  Copyright (c) 2014 Aman Enterprises Inc. All rights reserved.
//

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

#include "nmea.h"

@interface SatellitesView : NSObject

- (UIViewController *)getSatellitesViewController;  // get the view controller to display the GPS View

////
// Let this object manage the nmea data to update the display
//
////
- (void)updateViewWith:(nmeaINFO)info;

// init or reinit the satellites views (on cable connection / disconnection for example)
- (void)initSatellitesViews;

- (NSString *)version;      // ex: "1.0"

@end
