//
//  ConfigurationTableViewController.m
// SDK Sample
//
//  Created by Sharad Garg on 11/12/14.
//  Copyright (c) 2014 Aman Enterprises Inc. All rights reserved.
//

#import "ConfigurationTableViewController.h"

#import "GPSManager.h"
#include "DLog.h"

// sections
typedef NS_ENUM(NSUInteger, configurationSection) {
	sectionCable = 0,
    sectionStats,
    sectionsNbr
};

// cable cells
typedef NS_ENUM(NSUInteger, cableRow) {
    rowCableStatus = 0,
    cableRowsNbr
};

// stats cells
typedef NS_ENUM(NSUInteger, statRow) {
    rowStatsRx=0,
    statsRowsNbr
};


@interface ConfigurationTableViewController ()

@end

@implementation ConfigurationTableViewController

- (id)initWithStyle:(UITableViewStyle)style
{
    self = [super initWithStyle:style];
    if (self) {
        // Custom initialization
    }
    return self;
}

- (void)viewDidLoad
{
    [super viewDidLoad];
    
}

- (void)viewWillAppear:(BOOL)animated {
    DLog(@"enter");
    [super viewWillAppear:animated];
}

- (void)viewDidAppear:(BOOL)animated {
    DLog(@"enter");
    [super viewDidAppear:animated];
    
    // reload all our data
    [self.tableView reloadData];
}

- (void)didReceiveMemoryWarning
{
    [super didReceiveMemoryWarning];
    // Dispose of any resources that can be recreated.
}

#pragma mark - Table view data source

- (NSInteger)numberOfSectionsInTableView:(UITableView *)tableView
{
    // Return the number of sections.
    return sectionsNbr;
}

- (NSInteger)tableView:(UITableView *)tableView numberOfRowsInSection:(NSInteger)section
{
    //DLog(@"enter for section %d", section);
    NSInteger nRows = 0;
	switch(section)
	{
		case sectionCable:
			nRows = cableRowsNbr;
			break;
		case sectionStats:
			nRows = statsRowsNbr;
			break;
		default:
            DLog(@"?? unknown section");
			nRows = 0;
			break;
	}
	
	return nRows;

}

- (UITableViewCell *)tableView:(UITableView *)tableView cellForRowAtIndexPath:(NSIndexPath *)indexPath
{
    //DLog(@"enter at path %d %d", indexPath.section, indexPath.row);
    
    static NSString *configurationCell = @"configurationCell";

    
    UITableViewCell *cell;
    // Get a resused cell

    cell = [tableView dequeueReusableCellWithIdentifier:configurationCell];
    
    CGFloat fontSize = cell.textLabel.font.pointSize;
    cell.textLabel.font = [UIFont boldSystemFontOfSize:fontSize];
    cell.detailTextLabel.textColor = [UIColor blackColor];
    
    // configure cell
    // background color
	if (indexPath.row % 2)
	{
        [cell setBackgroundColor:[UIColor colorWithRed:.51 green:.51 blue:.51 alpha:1]];
	}
	else [cell setBackgroundColor:[UIColor colorWithRed:.61 green:.61 blue:.61 alpha:1]];
	
    // content
	if (indexPath.section == sectionCable) {
        // cable section
        if (indexPath.row == rowCableStatus) {
            // status
            cell.textLabel.text = @"Connection status";
			cell.detailTextLabel.text = ([[ConnectionManager sharedConnectionManager] isConnectionConnected]) ? @"Connected" : @"Not connected";
            cell.accessoryType = UITableViewCellAccessoryNone;
        }
    }
    else if (indexPath.section == sectionStats) {
        // stats
        if (indexPath.row == rowStatsRx)
        {
            cell.textLabel.text = @"Rx";
            cell.detailTextLabel.text = [NSString stringWithFormat:@"%d", [[ConnectionManager sharedConnectionManager] getRxCounter]];
        }
    }
    return cell;
}

#pragma mark - Table view delegate

- (void)tableView:(UITableView *)tableView didSelectRowAtIndexPath:(NSIndexPath *)indexPath {
    DLog(@"enter %d %d", indexPath.section, indexPath.row);
    // deselect
    [tableView deselectRowAtIndexPath:indexPath animated:YES];
}

#pragma mark -
#pragma mark ConnectionManager delegate
- (void)connectionConnected:(NSString *)protocol
{
	DLog(@"enter");
    // get cell for cable status
	NSIndexPath *path = [NSIndexPath indexPathForRow:rowCableStatus inSection:sectionCable];
	UITableViewCell *cell = [self.tableView cellForRowAtIndexPath:path];
	
	cell.detailTextLabel.text = @"Connected";
    [[ConnectionManager sharedConnectionManager] setBaud:DEFAULT_BAUD];
    [self.tableView reloadRowsAtIndexPaths:@[path] withRowAnimation:UITableViewRowAnimationAutomatic];
}

- (void)connectionDisconnected
{
	DLog(@"enter");
    // get cell for device status
	NSIndexPath *path = [NSIndexPath indexPathForRow:rowCableStatus inSection:sectionCable];
	UITableViewCell *cell = [self.tableView cellForRowAtIndexPath:path];
	cell.detailTextLabel.text = @"Not connected";
    [self.tableView reloadRowsAtIndexPaths:@[path] withRowAnimation:UITableViewRowAnimationAutomatic];
    
    // clear stats
    [self updateStats];
}

- (void)updateStats
{
	DLog(@"enter");
    NSIndexPath *path = [NSIndexPath indexPathForRow:rowStatsRx inSection:sectionStats];
	UITableViewCell *cell = [self.tableView cellForRowAtIndexPath:path];
	cell.detailTextLabel.text = [NSString stringWithFormat:@"%d", [[ConnectionManager sharedConnectionManager] getRxCounter]];
    
	cell = [self.tableView cellForRowAtIndexPath:path];
    [self.tableView reloadSections:[NSIndexSet indexSetWithIndex:sectionStats] withRowAnimation:UITableViewRowAnimationAutomatic];
}


@end
